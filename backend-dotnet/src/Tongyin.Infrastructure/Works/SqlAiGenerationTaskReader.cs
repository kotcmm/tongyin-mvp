using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Works;

namespace Tongyin.Infrastructure.Works;

/// <summary>
/// 基于现有作品 JSON 的完整 AI 生命周期只读投影。
/// 历史记录没有保存服务商 HTTP 回执时，只呈现已知状态，不冒充实时进度。
/// </summary>
public sealed class SqlAiGenerationTaskReader(Func<DbConnection> connections) : IAiGenerationTaskReader
{
    /// <summary>图片、3D 独立筛选，包含排队、执行、超时、完成和失败状态。</summary>
    public async Task<IReadOnlyList<AiGenerationTask>> ListAsync(
        string stage, CancellationToken cancellationToken = default)
    {
        if (stage is not ("image" or "model"))
        {
            throw new ArgumentException("仅支持 image 和 model 生成阶段", nameof(stage));
        }

        await using var db = connections();
        await db.OpenAsync(cancellationToken);
        var payloads = await db.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works ORDER BY created_at DESC, id DESC",
            cancellationToken: cancellationToken));

        var modelJobs = stage == "model"
            ? (await db.QueryAsync<ModelJobRow>(new CommandDefinition("""
                SELECT work_id AS WorkId, generation_id AS GenerationId,
                    provider_job_id AS ProviderJobId, status AS Status,
                    provider_id AS ProviderId, prompt_version AS PromptVersion,
                    updated_at AS UpdatedAt FROM ty_model_jobs
                """, cancellationToken: cancellationToken)))
                .GroupBy(job => job.WorkId)
                .ToDictionary(group => group.Key, group => group.First())
            : new Dictionary<string, ModelJobRow>();
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<AiGenerationTask>();
        foreach (var payload in payloads)
        {
            using var record = JsonDocument.Parse(payload);
            var root = record.RootElement;
            var work = root.Deserialize<WorkSnapshot>()
                ?? throw new JsonException("作品记录不是合法的 JSON 对象");
            var hasImageResult = work.Candidates.Count > 0;
            var isImageTask = work.ImageAttempts > 0 ||
                hasImageResult || work.Status is "image_ready" or "model_pending" or "model_ready";
            var isModelTask = work.Selected == 0 && hasImageResult
                && work.Status is ("model_pending" or "model_ready" or "failed");

            if (stage == "image" && !isImageTask || stage == "model" && !isModelTask)
            {
                continue;
            }

            modelJobs.TryGetValue(work.Id, out var providerJob);
            var stageMatches = work.AiTaskStage == stage;
            var recordedClaim = !string.IsNullOrWhiteSpace(work.AiExecutionClaimId)
                && work.AiExecutionClaimId == work.GenerationId;
            var started = stageMatches &&
                DateTimeOffset.TryParse(work.AiStartedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var startTime)
                ? startTime : (DateTimeOffset?)null;
            var complete = stage == "image"
                ? hasImageResult || work.Status == "image_ready"
                : work.Status == "model_ready" && !string.IsNullOrWhiteSpace(work.Model);
            var failed = stage == "image"
                ? work.Status == "failed" && !hasImageResult
                : work.Status == "failed" && hasImageResult;
            var hasClaim = stageMatches && (recordedClaim || started.HasValue);
            var stale = !complete && !failed && hasClaim &&
                started.HasValue && started.Value <= now.AddMinutes(-15);

            // 人工核查端还会验证任务编号、阶段及数据库认领状态。
            var canReview = stale && !string.IsNullOrWhiteSpace(work.GenerationId)
                && (string.IsNullOrWhiteSpace(work.AiExecutionClaimId) || recordedClaim)
                && (stage == "image" || work.Selected == 0 && hasImageResult);
            // 已获得混元 JobId 并正常轮询的任务，不是待人工恢复的未知提交。
            if (stage == "model" && providerJob is not null)
            {
                var outcomeKnown = providerJob.Status is "WAIT" or "RUN";
                canReview = !outcomeKnown && providerJob.Status == "submitting" && stale;
            }

            var status = complete ? "completed" : failed ? "failed"
                : stale ? "stalled" : hasClaim ? "processing" : "queued";
            var apiPhase = complete ? "result_saved" : failed ? "failed"
                : hasClaim ? "claimed_outcome_unconfirmed" : "not_dispatched";
            if (stage == "model" && providerJob is not null && !complete && !failed)
            {
                apiPhase = providerJob.Status switch
                {
                    "WAIT" => "provider_wait",
                    "RUN" => "provider_run",
                    "submitting" => "submission_uncertain",
                    _ => apiPhase
                };
                // 混元报告 WAIT/RUN 后，必须等待真实完成结果而不是仅因 15 分钟判定超时。
                if (providerJob.Status is "WAIT" or "RUN") status = "processing";
            }
            tasks.Add(new AiGenerationTask
            {
                WorkId = work.Id,
                // 若已开始模型阶段，原图片生成的任务编号可能已被覆盖，不再冒充原图任务编号。
                GenerationId = stage == "image" && work.AiTaskStage == "model"
                    ? string.Empty : work.GenerationId,
                Stage = stage,
                Status = status,
                ApiPhase = apiPhase,
                CreatedAt = work.CreatedAt,
                StartedAt = started?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                ProviderId = stage == "model" && providerJob is not null
                    ? providerJob.ProviderId : stageMatches ? work.AiProviderId : string.Empty,
                PromptVersion = stage == "model" && providerJob is not null
                    ? providerJob.PromptVersion : stageMatches ? work.AiPromptVersion : 0,
                ImageAttempts = work.ImageAttempts,
                Note = failed || stale ? work.Note : string.Empty,
                CanReview = canReview,
                ProviderJobId = stage == "model" ? providerJob?.ProviderJobId ?? string.Empty : string.Empty
            });
        }

        return tasks;
    }

    private sealed class ModelJobRow
    {
        public string WorkId { get; set; } = "";
        public string GenerationId { get; set; } = "";
        public string? ProviderJobId { get; set; }
        public string Status { get; set; } = "";
        public string ProviderId { get; set; } = "";
        public int PromptVersion { get; set; }
        public string UpdatedAt { get; set; } = "";
    }
}
