using System.Data.Common;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Auth;

namespace Tongyin.Infrastructure.Auth;

/// <summary>
/// 从现有 Go 服务的 ty_users 表检查用户身份及账号禁用状态。
/// </summary>
public sealed class SqlUserAccess : IWechatUserRepository
{
    private readonly Func<DbConnection> _connectionFactory;

    /// <summary>
    /// 使用共享的数据库连接工厂读取旧版用户资料。
    /// </summary>
    public SqlUserAccess(Func<DbConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// 查找或原子创建微信用户；已经停用的账户不会被修改或重新激活。
    /// </summary>
    public async Task<bool> FindOrCreateEnabledAsync(string openId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(openId))
        {
            throw new ArgumentException("OpenID 不能为空", nameof(openId));
        }

        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // 与作品操作使用同一数据库锁；并发登录同一 OpenID 只能创建一次账户。
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_store_lock SET id = id WHERE id = 1",
            transaction: transaction, cancellationToken: cancellationToken));
        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_users WHERE owner_id = @OpenId",
            new { OpenId = openId }, transaction: transaction, cancellationToken: cancellationToken));

        if (payload is not null)
        {
            using var document = JsonDocument.Parse(payload);
            var disabled = document.RootElement.TryGetProperty("disabled", out var value)
                && value.ValueKind != JsonValueKind.False;
            await transaction.CommitAsync(cancellationToken);
            return !disabled;
        }

        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow.ToString("O");
        var userPayload = JsonSerializer.Serialize(new { id, owner = openId, created_at = createdAt, disabled = false });
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO ty_users (id, owner_id, created_at, payload) VALUES (@Id, @OwnerId, @CreatedAt, @Payload)",
            new { Id = id, OwnerId = openId, CreatedAt = createdAt, Payload = userPayload },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// 只有已登记且未停用的微信用户可以继续使用需要身份验证的业务。
    /// </summary>
    public async Task<bool> IsEnabledAsync(string owner, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_users WHERE owner_id = @Owner",
            new { Owner = owner }, cancellationToken: cancellationToken));
        if (payload is null)
        {
            return false;
        }

        using var document = JsonDocument.Parse(payload);
        return !document.RootElement.TryGetProperty("disabled", out var disabled)
            || disabled.ValueKind == JsonValueKind.False;
    }
}
