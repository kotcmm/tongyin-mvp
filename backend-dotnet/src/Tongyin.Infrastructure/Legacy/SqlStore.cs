using System.Data.Common;
using System.Text.Json.Nodes;
using MySqlConnector;
using Npgsql;

namespace Tongyin.Api.Core;

/// <summary>
/// 兼容 PostgreSQL 与 MySQL 的数据库存储实现。
/// </summary>
public sealed class SqlStore(string driver, string connectionString) : IStore
{
    private DbConnection OpenConnection() => driver switch
    {
        "postgres" => new NpgsqlConnection(NpgsqlString(connectionString)),
        "mysql" => new MySqlConnection(MySqlString(connectionString)),
        _ => throw new InvalidOperationException("不支持此数据库")
    };
    private static string NpgsqlString(string original)
    {
        if (!original.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !original.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)) return original;
        var u = new Uri(original);
        var cred = u.UserInfo.Split(':', 2);
        var p = new NpgsqlConnectionStringBuilder
        {
            Host = u.Host, Port = u.Port > 0 ? u.Port : 5432,
            Username = Uri.UnescapeDataString(cred[0]),
            Password = cred.Length > 1 ? Uri.UnescapeDataString(cred[1]) : "",
            Database = u.AbsolutePath.TrimStart('/'),
            SslMode = Npgsql.SslMode.Disable
        };
        foreach (var pair in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv[0] == "sslmode")
                p.SslMode = kv.Length > 1 && kv[1].Equals("require", StringComparison.OrdinalIgnoreCase)
                    ? Npgsql.SslMode.Require : Npgsql.SslMode.Disable;
        }
        return p.ConnectionString;
    }
    private static string MySqlString(string original)
    {
        if (!original.Contains("@tcp(", StringComparison.Ordinal)) return original;
        var at = original.IndexOf("@tcp(", StringComparison.Ordinal);
        var auth = original[..at].Split(':', 2);
        var end = original.IndexOf(')', at);
        if (end < 0) throw new InvalidOperationException("MySQL Go DSN 格式错误");
        var host = original[(at + 5)..end];
        var database = original[(end + 2)..].Split('?')[0];
        var h = host.Split(':', 2);
        var b = new MySqlConnectionStringBuilder
        {
            Server = h[0], Port = h.Length > 1 ? uint.Parse(h[1]) : 3306,
            UserID = auth[0], Password = auth.Length > 1 ? auth[1] : "",
            Database = database, CharacterSet = "utf8mb4", SslMode = MySqlSslMode.Preferred
        };
        return b.ConnectionString;
    }
    /// <summary>
    /// 初始化兼容数据表结构。
    /// </summary>
    public async Task Initialize(CancellationToken ct = default)
    {
        await using var con = OpenConnection();
        await con.OpenAsync(ct);
        foreach (var table in new Snapshot().Sections().Select(s => s.Table))
        {
            var idLen = table is "ty_works" or "ty_orders" or "ty_audit" or "ty_ai_providers" or "ty_ai_prompts" ? 32 : 128;
            await using var cmd = con.CreateCommand();
            cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {table} (id VARCHAR({idLen}) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL)" +
                              (driver == "mysql" ? " ENGINE=InnoDB DEFAULT CHARSET=utf8mb4" : "");
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS ty_store_lock (id INTEGER PRIMARY KEY)" +
                              (driver == "mysql" ? " ENGINE=InnoDB" : "");
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = driver == "mysql" ? "INSERT IGNORE INTO ty_store_lock(id) VALUES(1)" :
                                               "INSERT INTO ty_store_lock(id) VALUES(1) ON CONFLICT (id) DO NOTHING";
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
    private async Task<Snapshot> Load(DbConnection con, DbTransaction? tx, CancellationToken ct)
    {
        var result = new Snapshot();
        foreach (var (table, docs) in result.Sections())
        {
            await using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT id,payload FROM {table}";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                docs[reader.GetString(0)] = Doc.Parse(reader.GetString(1));
        }
        return result;
    }
    public async Task<T> Read<T>(Func<Snapshot,T> fn, CancellationToken ct = default)
    {
        await using var con = OpenConnection();
        await con.OpenAsync(ct);
        return fn(await Load(con, null, ct));
    }
    public async Task<T> Update<T>(Func<Snapshot,T> fn, CancellationToken ct = default)
    {
        await using var con = OpenConnection();
        await con.OpenAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        try
        {
            await using (var locking = con.CreateCommand())
            {
                locking.Transaction = tx;
                locking.CommandText = "SELECT id FROM ty_store_lock WHERE id=1 FOR UPDATE";
                _ = await locking.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("数据库锁不可用");
            }
            var original = await Load(con, tx, ct);
            var changed = original.DeepClone();
            var output = fn(changed);
            var before = original.Sections().ToArray();
            var after = changed.Sections().ToArray();
            for (var i = 0; i < before.Length; i++)
            {
                var table = before[i].Table;
                var previous = before[i].Values;
                var next = after[i].Values;
                foreach (var removed in previous.Keys.Except(next.Keys))
                    await Execute(con, tx, $"DELETE FROM {table} WHERE id=@id", ct, ("id", removed));
                foreach (var (id, doc) in next)
                {
                    var payload = doc.ToJsonString();
                    if (previous.TryGetValue(id, out var old) && old.ToJsonString() == payload) continue;
                    var owner = Doc.Str(doc, "owner");
                    var created = Doc.Str(doc, "created_at");
                    if (previous.ContainsKey(id))
                        await Execute(con, tx, $"UPDATE {table} SET owner_id=@owner,created_at=@created,payload=@payload WHERE id=@id", ct,
                            ("owner",owner),("created",created),("payload",payload),("id",id));
                    else
                        await Execute(con, tx, $"INSERT INTO {table}(id,owner_id,created_at,payload) VALUES(@id,@owner,@created,@payload)", ct,
                            ("id",id),("owner",owner),("created",created),("payload",payload));
                }
            }
            await tx.CommitAsync(ct);
            return output;
        }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }
    private static async Task Execute(DbConnection con, DbTransaction tx, string sql, CancellationToken ct, params (string Key,string Value)[] parameters)
    {
        await using var command = con.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (key, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + key;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        await command.ExecuteNonQueryAsync(ct);
    }
}
