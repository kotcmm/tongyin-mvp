using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;

namespace Tongyin.Infrastructure.Works;

/// <summary>
/// 人工核查图片任务时，使用统一数据库事务锁，同时写入作品和 ty_audit 审计。
/// </summary>
public sealed class SqlAdminImageReviewRepository(Func<DbConnection> connectionFactory) : IAdminImageReviewRepository
{
    /// <summary>
    /// 仅允许处理认领已超过 15 分钟、状态和编号仍匹配的任务；状态与审计必须一起提交。
    /// </summary>
    public async Task<WorkSnapshot> ResolveAsync(string workId, string generationId, string actor, string reason, string? fileName, DateTimeOffset reviewedAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workId) || string.IsNullOrWhiteSpace(generationId) || string.IsNullOrWhiteSpace(actor)
            || string.IsNullOrWhiteSpace(reason) || reason.Length > 300)
        {
            throw new ArgumentException("人工核查需要任务编号、管理员身份和处理依据");
        }

        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // 与其它作品写操作串行，避免回写时已生成了更新的任务。
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_store_lock SET id = id WHERE id = 1",
            transaction: transaction, cancellationToken: cancellationToken));
        await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT id FROM ty_store_lock WHERE id = 1",
            transaction: transaction, cancellationToken: cancellationToken));

        var record = await connection.QuerySingleOrDefaultAsync<(string Owner, string Payload)>(new CommandDefinition(
            "SELECT owner_id AS Owner, payload AS Payload FROM ty_works WHERE id = @Id",
            new { Id = workId }, transaction: transaction, cancellationToken: cancellationToken));
        if (record.Payload is null)
        {
            throw new InvalidOperationException("待核查任务已不存在");
        }

        var snapshot = JsonSerializer.Deserialize<WorkSnapshot>(record.Payload)
            ?? throw new JsonException("作品记录格式错误");
        if (snapshot.Status != "image_pending" || snapshot.GenerationId != generationId
            || snapshot.AiTaskStage != "image"
            || snapshot.AiExecutionClaimId != generationId
            || !DateTimeOffset.TryParse(snapshot.AiStartedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startedAt)
            || startedAt > reviewedAt.AddMinutes(-15))
        {
            throw new InvalidOperationException("任务不满足人工核查条件，可能已完成或已更新");
        }

        var work = Work.Restore(
            workId, record.Owner, DateTimeOffset.Parse(snapshot.CreatedAt, CultureInfo.InvariantCulture),
            snapshot.Original, WorkStatus.ImagePending, snapshot.Selected, snapshot.ImageAttempts,
            snapshot.GenerationId, snapshot.Candidates, snapshot.Model, snapshot.Note);

        var payload = JsonNode.Parse(record.Payload) as JsonObject
            ?? throw new JsonException("作品记录格式错误");
        var completed = fileName is not null;
        if (completed)
        {
            work.CompleteImageGeneration(generationId, fileName!);
            payload["status"] = "image_ready";
            payload["candidates"] = new JsonArray(JsonValue.Create(fileName));
        }
        else
        {
            work.FailImageGeneration(generationId, "AI 生成失败，请联系客服");
            payload["status"] = "failed";
            payload["note"] = work.Note;
        }

        var auditId = Guid.NewGuid().ToString("N");
        var timestamp = reviewedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var action = completed ? "ai.image.review.completed" : "ai.image.review.failed";
        var auditPayload = JsonSerializer.Serialize(new
        {
            id = auditId,
            owner = actor,
            created_at = timestamp,
            action,
            target = workId,
            detail = JsonSerializer.Serialize(new { generation_id = generationId, reason, file_name = fileName ?? "" })
        });

        var updatedPayload = payload.ToJsonString();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_works SET payload = @Payload WHERE id = @Id",
            new { Payload = updatedPayload, Id = workId },
            transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO ty_audit(id, owner_id, created_at, payload) VALUES (@Id, @Owner, @CreatedAt, @Payload)",
            new { Id = auditId, Owner = actor, CreatedAt = timestamp, Payload = auditPayload },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return JsonSerializer.Deserialize<WorkSnapshot>(updatedPayload)
            ?? throw new JsonException("更新后的作品记录格式错误");
    }
}
