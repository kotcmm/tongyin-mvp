using System.Data.Common;
using Dapper;
using Tongyin.Application.Admin;

namespace Tongyin.Infrastructure.Auth;

/// <summary>管理员登录凭据使用单独的数据表，兼容 PostgreSQL 和 MySQL。</summary>
public sealed class SqlAdminCredentialsStore(Func<DbConnection> connections) : IAdminCredentialsStore
{
    /// <summary>读取单管理员账户及密码派生摘要，不返回明文口令。</summary>
    public async Task<AdminCredentials?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AdminCredentials>(new CommandDefinition(
            "SELECT username AS Username, password_salt AS PasswordSalt, password_hash AS PasswordHash, session_version AS SessionVersion FROM ty_admin_credentials WHERE id=1",
            cancellationToken: cancellationToken));
    }

    /// <summary>仅在表为空时插入初始账号，防止环境变量重置现有密码。</summary>
    public async Task<bool> CreateIfMissingAsync(AdminCredentials account, CancellationToken cancellationToken = default)
    {
        await using var connection = connections();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO ty_admin_credentials (id,username,password_salt,password_hash,session_version) SELECT 1,@Username,@PasswordSalt,@PasswordHash,@SessionVersion WHERE NOT EXISTS (SELECT 1 FROM ty_admin_credentials WHERE id=1)",
            account, cancellationToken: cancellationToken)) == 1;
    }

    /// <summary>对比旧会话版本后原子写入新凭据，阻止并发修改丢失。</summary>
    public async Task<bool> UpdateIfVersionAsync(AdminCredentials updated, string previousVersion, CancellationToken cancellationToken = default)
    {
        await using var connection = connections();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_admin_credentials SET username=@Username,password_salt=@PasswordSalt,password_hash=@PasswordHash,session_version=@SessionVersion WHERE id=1 AND session_version=@PreviousVersion",
            new { updated.Username, updated.PasswordSalt, updated.PasswordHash, updated.SessionVersion, PreviousVersion = previousVersion },
            cancellationToken: cancellationToken)) == 1;
    }
}
