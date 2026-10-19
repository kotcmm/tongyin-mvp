using System.Data.Common;
using System.Text.Json;
using Dapper;

namespace Tongyin.Infrastructure.Media;

/// <summary>
/// 安全处理作品删除时写入的媒体待清理队列。
/// 每个文件与作品的数据库引用检查和磁盘删除均在与作品写入相同的数据库锁事务中执行。
/// </summary>
public sealed class PendingMediaCleanup(Func<DbConnection> connections, LocalMediaStore media)
{
    /// <summary>每次处理一条持久化任务；无任务返回 false，磁盘失败回滚以供重试。</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        await using var db = connections();
        await db.OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);

        // 作品创建、删除和生成任务均使用该事务行锁，避免引用检查与删除交错。
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_store_lock SET id=id WHERE id=1", transaction: tx, cancellationToken: cancellationToken));

        var fileName = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT file_name FROM ty_media_cleanup ORDER BY requested_at, file_name LIMIT 1",
            transaction: tx, cancellationToken: cancellationToken));
        if (fileName is null)
        {
            return false;
        }

        // 只允许本程序可识别的文件名，队列不能成为任意磁盘路径删除接口。
        if (!LocalMediaStore.IsStoredMediaName(fileName))
        {
            await RemoveQueueEntryAsync(db, tx, fileName, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return true;
        }

        // 同一媒体可能被其他作品引用；历史订单快照也有可能直接保存媒体名称。
        // 对损坏的 JSON 采取保守策略：抛错并保留任务，不能冒险删除。
        var works = await db.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works", transaction: tx, cancellationToken: cancellationToken));
        var orders = await db.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders", transaction: tx, cancellationToken: cancellationToken));
        var isReferenced = works.Any(payload => ReferencesMedia(payload, fileName))
            || orders.Any(payload => ReferencesMedia(payload, fileName));

        if (!isReferenced)
        {
            // 文件可能已经由之前崩溃的处理尝试删除：File.Delete 本身是幂等操作。
            // 磁盘出错时不要提交队列删除；下次启动仍可重试。
            media.DeleteStoredMedia(fileName);
        }

        // 如果其它作品仍引用媒体，本次只放弃回收；那个作品后来删除时会重新登记。
        await RemoveQueueEntryAsync(db, tx, fileName, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    private static Task<int> RemoveQueueEntryAsync(
        DbConnection db, DbTransaction tx, string name, CancellationToken ct)
        => db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ty_media_cleanup WHERE file_name=@Name", new { Name = name },
            transaction: tx, cancellationToken: ct));

    private static bool ReferencesMedia(string payload, string name)
    {
        using var json = JsonDocument.Parse(payload);
        return ContainsReference(json.RootElement, name);
    }

    private static bool ContainsReference(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                return value == name || value?.Contains("/media/" + name, StringComparison.Ordinal) == true;
            case JsonValueKind.Object:
                return element.EnumerateObject().Any(property => ContainsReference(property.Value, name));
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(item => ContainsReference(item, name));
            default:
                return false;
        }
    }
}
