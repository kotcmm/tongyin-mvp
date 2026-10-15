using System.Data.Common;
using System.Text.Json;
using Dapper;

namespace Tongyin.Infrastructure.Auth;

/// <summary>
/// 从现有 Go 服务的 ty_users 表检查用户身份及账号禁用状态。
/// </summary>
public sealed class SqlUserAccess
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
            || disabled.ValueKind != JsonValueKind.True;
    }
}
