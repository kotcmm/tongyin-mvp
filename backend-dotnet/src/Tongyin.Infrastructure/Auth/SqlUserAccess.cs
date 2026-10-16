using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Tongyin.Application.Auth;

namespace Tongyin.Infrastructure.Auth;

/// <summary>
/// 管理 ty_users 微信用户注册、登录时间和授权状态；兼容 Go 的 OpenID 主键规则。
/// </summary>
public sealed class SqlUserAccess : IWechatUserRepository
{
    private readonly Func<DbConnection> _connectionFactory;

    /// <summary>使用跨 PostgreSQL、MySQL 的数据库连接工厂。</summary>
    public SqlUserAccess(Func<DbConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>首次注册使用 OpenID 作为数据库主键和独立的 24 位内部 ID；已有用户保留原始内部 ID。</summary>
    public async Task<bool> FindOrCreateEnabledAsync(string openId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(openId))
        {
            throw new ArgumentException("OpenID 不能为空", nameof(openId));
        }

        await using var db = _connectionFactory();
        await db.OpenAsync(cancellationToken);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        // 与旧 Go 后端共用 store_lock 串行化用户写入，避免重复注册。
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_store_lock SET id = id WHERE id = 1",
            transaction: transaction, cancellationToken: cancellationToken));

        var payload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_users WHERE owner_id = @OpenId",
            new { OpenId = openId }, transaction: transaction, cancellationToken: cancellationToken));

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        if (payload is not null)
        {
            var document = JsonNode.Parse(payload) as JsonObject
                ?? throw new JsonException("用户档案 JSON 格式无效");
            // 显式禁用的账户不得被重新激活、迁移或更新。
            if (document.ContainsKey("disabled") && document["disabled"]?.ToJsonString() != "false")
            {
                return false;
            }

            document["last_login"] = timestamp;
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE ty_users SET id = @OpenId, payload = @Payload WHERE owner_id = @OpenId",
                new { OpenId = openId, Payload = document.ToJsonString() },
                transaction: transaction, cancellationToken: cancellationToken));
        }
        else
        {
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            var payload = JsonSerializer.Serialize(new
            {
                id,
                owner = openId,
                created_at = timestamp,
                last_login = timestamp,
                disabled = false
            });
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ty_users (id, owner_id, created_at, payload) VALUES (@OpenId, @OpenId, @Timestamp, @Payload)",
                new { OpenId = openId, Timestamp = timestamp, Payload = payload },
                transaction: transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>仅已存在且未被停用的微信用户可访问受保护业务。</summary>
    public async Task<bool> IsEnabledAsync(string owner, CancellationToken cancellationToken = default)
    {
        await using var db = _connectionFactory();
        await db.OpenAsync(cancellationToken);
        var payload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
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

    /// <summary>从持久化用户信息获取公开资料，按 OpenID 统计本人的作品与订单。</summary>
    public async Task<WechatUserProfile?> GetProfileAsync(string openId, CancellationToken cancellationToken = default)
    {
        await using var db = _connectionFactory();
        await db.OpenAsync(cancellationToken);
        var payload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_users WHERE owner_id = @OpenId",
            new { OpenId = openId }, cancellationToken: cancellationToken));
        if (payload is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var userId) && userId.ValueKind == JsonValueKind.String
            ? userId.GetString() : null;
        var createdAt = root.TryGetProperty("created_at", out var created) && created.ValueKind == JsonValueKind.String
            ? created.GetString() : null;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(createdAt))
        {
            throw new JsonException("用户档案缺少内部 ID 或创建时间");
        }

        var works = await db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM ty_works WHERE owner_id = @OpenId",
            new { OpenId = openId }, cancellationToken: cancellationToken));
        var orders = await db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM ty_orders WHERE owner_id = @OpenId",
            new { OpenId = openId }, cancellationToken: cancellationToken));

        return new WechatUserProfile
        {
            Id = id,
            CreatedAt = createdAt,
            Works = checked((int)works),
            Orders = checked((int)orders)
        };
    }
}
