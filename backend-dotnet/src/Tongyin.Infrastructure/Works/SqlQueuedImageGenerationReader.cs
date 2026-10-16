using System.Data.Common;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Works;

namespace Tongyin.Infrastructure.Works;

/// <summary>
/// 从 Go 兼容的 ty_works 表只读获取 .NET 已登记、尚未认领的图片生成任务。
/// </summary>
public sealed class SqlQueuedImageGenerationReader(Func<DbConnection> connectionFactory) : IQueuedImageGenerationReader
{
    /// <summary>
    /// 按作品创建时间最早优先发现待派发任务，排除已执行及旧 Go 未标记的作品。
    /// </summary>
    public async Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 25)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "一次最多扫描 25 个待派发任务");
        }

        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        // 为兼容 PostgreSQL/MySQL 的 payload TEXT，筛选不依赖特定数据库的 JSON 语法。
        var rows = await connection.QueryAsync<(string Id, string Owner, string Payload)>(new CommandDefinition(
            "SELECT id, owner_id, payload FROM ty_works ORDER BY created_at ASC, id ASC",
            cancellationToken: cancellationToken));

        var queued = new List<QueuedImageGeneration>(limit);
        foreach (var row in rows)
        {
            using var document = JsonDocument.Parse(row.Payload);
            // Go 旧任务没有此字段；只有 .NET 原子登记的新任务才允许自动派发。
            if (!document.RootElement.TryGetProperty("ai_execution_claim_id", out var marker)
                || marker.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var work = document.RootElement.Deserialize<WorkSnapshot>()
                ?? throw new JsonException("作品记录不是合法的 JSON 对象");
            if (work.Status != "image_pending" || work.ImageAttempts < 1
                || string.IsNullOrEmpty(work.GenerationId) || !string.IsNullOrEmpty(work.AiExecutionClaimId)
                || !string.IsNullOrEmpty(work.AiTaskStage) || !string.IsNullOrEmpty(work.AiStartedAt))
            {
                continue;
            }

            queued.Add(new QueuedImageGeneration
            {
                WorkId = row.Id,
                Owner = row.Owner,
                GenerationId = work.GenerationId
            });
            if (queued.Count >= limit)
            {
                break;
            }
        }

        return queued;
    }
}
