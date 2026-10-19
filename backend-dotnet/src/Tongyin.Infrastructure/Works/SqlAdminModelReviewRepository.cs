using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;

namespace Tongyin.Infrastructure.Works;

/// <summary>管理员人工核查模型任务的事务实现，共用统一数据库事务锁与操作审计。</summary>
public sealed class SqlAdminModelReviewRepository(Func<DbConnection> connectionFactory) : IAdminModelReviewRepository
{
    /// <summary>检查当前任务编号、执行阶段和超时阈值，在同一事务内更新作品并写入审计。</summary>
    public async Task<WorkSnapshot> ResolveAsync(string workId, string generationId, string actor, string reason, string? fileName, DateTimeOffset reviewedAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workId) || string.IsNullOrWhiteSpace(generationId) || string.IsNullOrWhiteSpace(actor)
            || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 300)
        {
            throw new ArgumentException("模型人工核查需要有效任务编号和核实依据");
        }

        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
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
            throw new InvalidOperationException("待核查模型任务已不存在");
        }

        var snapshot = JsonSerializer.Deserialize<WorkSnapshot>(record.Payload)
            ?? throw new JsonException("作品记录格式错误");
        if (snapshot.Status != "model_pending" || snapshot.GenerationId != generationId
            || snapshot.Selected != 0 || snapshot.Candidates.Count == 0
            || snapshot.AiTaskStage != "model"
            || snapshot.AiExecutionClaimId != generationId
            || !DateTimeOffset.TryParse(snapshot.AiStartedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startedAt)
            || startedAt > reviewedAt.AddMinutes(-15))
        {
            throw new InvalidOperationException("任务不满足模型人工核查条件，可能已经完成或被更新");
        }

        var currentJobStatus = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM ty_model_jobs WHERE generation_id=@Id",
            new { Id = generationId }, transaction: transaction, cancellationToken: cancellationToken));
        if (currentJobStatus is "WAIT" or "RUN")
        {
            throw new InvalidOperationException("混元任务仍可轮询，不能人工结束；请等待真实结果");
        }

        var work = Work.Restore(
            workId, record.Owner,
            DateTimeOffset.Parse(snapshot.CreatedAt, CultureInfo.InvariantCulture),
            snapshot.Original, WorkStatus.ModelPending, snapshot.Selected, snapshot.ImageAttempts,
            snapshot.GenerationId, snapshot.Candidates, snapshot.Model, snapshot.Note);

        var completed = fileName is not null;
        if (completed)
        {
            work.CompleteModelGeneration(generationId, fileName!);
        }
        else
        {
            work.FailModelGeneration(generationId, "模型生成失败，请联系客服");
        }

        var payload = JsonNode.Parse(record.Payload) as JsonObject
            ?? throw new JsonException("作品记录格式错误");
        payload["status"] = completed ? "model_ready" : "failed";
        payload["model"] = work.Model;
        payload["note"] = work.Note;

        var auditId = Guid.NewGuid().ToString("N");
        var timestamp = reviewedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var auditPayload = JsonSerializer.Serialize(new
        {
            id = auditId,
            owner = actor,
            created_at = timestamp,
            action = completed ? "ai.model.review.completed" : "ai.model.review.failed",
            target = workId,
            detail = JsonSerializer.Serialize(new { generation_id = generationId, reason = reason.Trim(), file_name = fileName ?? "" })
        });

        var updated = payload.ToJsonString();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_works SET payload = @Payload WHERE id = @Id",
            new { Payload = updated, Id = workId },
            transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO ty_audit(id, owner_id, created_at, payload) VALUES (@Id, @Owner, @CreatedAt, @Payload)",
            new { Id = auditId, Owner = actor, CreatedAt = timestamp, Payload = auditPayload },
            transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_model_jobs SET status=@Status, updated_at=@At WHERE generation_id=@Id",
            new { Id = generationId, Status = completed ? "DONE" : "FAIL", At = reviewedAt.ToString("O") },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return JsonSerializer.Deserialize<WorkSnapshot>(updated)
            ?? throw new JsonException("更新后的模型记录格式错误");
    }
}
