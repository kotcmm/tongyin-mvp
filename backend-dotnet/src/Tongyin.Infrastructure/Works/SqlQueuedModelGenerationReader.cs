using System.Data.Common;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Works;

namespace Tongyin.Infrastructure.Works;

/// <summary>读取由 .NET 图片确认接口标记的模型待生成任务，忽略旧 Go 未标记的任务。</summary>
public sealed class SqlQueuedModelGenerationReader(Func<DbConnection> connectionFactory) : IQueuedModelGenerationReader
{
    /// <summary>最早优先发现模型待处理记录，不依赖 PostgreSQL/MySQL 特有 JSON 运算符。</summary>
    public async Task<IReadOnlyList<QueuedModelGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 25)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<(string Id, string Owner, string Payload)>(new CommandDefinition(
            "SELECT id, owner_id, payload FROM ty_works ORDER BY created_at ASC, id ASC",
            cancellationToken: cancellationToken));
        var queued = new List<QueuedModelGeneration>(limit);
        foreach (var row in rows)
        {
            using var doc = JsonDocument.Parse(row.Payload);
            // .NET 确认接口显式写入 ai_execution_claim_id，Go 旧任务没有此字段。
            if (!doc.RootElement.TryGetProperty("ai_execution_claim_id", out var marker)
                || marker.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var work = doc.RootElement.Deserialize<WorkSnapshot>()
                ?? throw new JsonException("作品记录不是合法的 JSON 对象");
            if (work.Status != "model_pending" || work.Selected != 0 || work.Candidates.Count == 0
                || string.IsNullOrWhiteSpace(work.GenerationId)
                || !string.IsNullOrEmpty(work.AiExecutionClaimId)
                || !string.IsNullOrEmpty(work.AiTaskStage)
                || !string.IsNullOrEmpty(work.AiStartedAt))
            {
                continue;
            }

            queued.Add(new QueuedModelGeneration
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
