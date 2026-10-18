using System.Data.Common;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Works;

namespace Tongyin.Infrastructure.Works;

/// <summary>通过 Go 兼容的 ty_works 表实现运营作品只读查询，支持 PostgreSQL 和 MySQL。</summary>
public sealed class SqlAdminWorksReader(Func<DbConnection> connectionFactory) : IAdminWorksReader
{
    /// <summary>按创建时间、作品 ID 倒序扫描已有记录，不修改作品或 AI 任务状态。</summary>
    public async Task<IReadOnlyList<WorkSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        var payloads = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works ORDER BY created_at DESC, id DESC",
            cancellationToken: cancellationToken));
        return payloads.Select(payload => JsonSerializer.Deserialize<WorkSnapshot>(payload)
            ?? throw new JsonException("作品记录格式无效")).ToArray();
    }
}
