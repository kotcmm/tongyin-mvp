using System.Data.Common;
using Dapper;
using Tongyin.Application.Works;

namespace Tongyin.Infrastructure.Works;

/// <summary>持久化混元 JobId 和上游状态；不再将外部任务生命周期塞进作品 JSON。</summary>
public sealed class SqlModelGenerationJobRepository(Func<DbConnection> connections) : IModelGenerationJobRepository
{
    /// <summary>获取可轮询任务；待提交或状态不确定任务绝不重新发送收费请求。</summary>
    public async Task<ModelGenerationJob?> GetNextSubmittedAsync(CancellationToken ct = default)
    {
        await using var db = connections();
        await db.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<ModelGenerationJob>(new CommandDefinition("""
            SELECT work_id AS WorkId, generation_id AS GenerationId,
                provider_job_id AS ProviderJobId, provider_id AS ProviderId,
                api_key_cipher AS ApiKeyCipher, model AS Model
            FROM ty_model_jobs WHERE status IN ('WAIT', 'RUN')
            ORDER BY updated_at ASC, generation_id ASC LIMIT 1
            """, cancellationToken: ct));
    }

    /// <summary>仅允许把一次已确认的提交响应变为可轮询状态。</summary>
    public async Task RecordSubmittedAsync(string generationId, string providerJobId,
        string apiKeyCipher, string model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerJobId) || providerJobId.Length > 128
            || !apiKeyCipher.StartsWith("dp1:", StringComparison.Ordinal)
            || model is not ("3.0" or "3.1"))
            throw new InvalidDataException("混元返回的 JobId 无效");
        await using var db = connections();
        await db.OpenAsync(ct);
        var changed = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE ty_model_jobs SET provider_job_id=@JobId, api_key_cipher=@Cipher,
                model=@Model, status='WAIT', updated_at=@At
            WHERE generation_id=@Id AND status='submitting' AND provider_job_id IS NULL
            """, new { Id = generationId, JobId = providerJobId, Cipher = apiKeyCipher, Model = model,
                At = DateTimeOffset.UtcNow.ToString("O") },
            cancellationToken: ct));
        if (changed != 1) throw new InvalidOperationException("模型提交记录已失效或重复");
    }

    /// <summary>按当前状态做条件更新，已终结任务不能重新变为可轮询状态。</summary>
    public async Task UpdateStatusAsync(string generationId, string status, CancellationToken ct = default)
    {
        if (status is not ("WAIT" or "RUN" or "DONE" or "FAIL"))
            throw new ArgumentOutOfRangeException(nameof(status));
        await using var db = connections();
        await db.OpenAsync(ct);
        var changed = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE ty_model_jobs SET status=@Status, updated_at=@At
            WHERE generation_id=@Id AND status IN ('WAIT','RUN')
            """, new { Id = generationId, Status = status, At = DateTimeOffset.UtcNow.ToString("O") },
            cancellationToken: ct));
        if (changed != 1) throw new InvalidOperationException("模型任务状态已改变");
    }
}
