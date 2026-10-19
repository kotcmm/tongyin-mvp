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

        var now = DateTimeOffset.UtcNow;
        var tasks = new List<AiGenerationTask>();
        foreach (var payload in payloads)
        {
            using var record = JsonDocument.Parse(payload);
            var root = record.RootElement;
            var hasDotnetQueueMarker = root.TryGetProperty("ai_execution_claim_id", out var marker)
                && marker.ValueKind == JsonValueKind.String;
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

            // 人工核查端还有更严格的任务编号、阶段和旧 Go 兼容校验。
            var canReview = stale && !string.IsNullOrWhiteSpace(work.GenerationId)
                && (string.IsNullOrWhiteSpace(work.AiExecutionClaimId) || recordedClaim)
                && (stage == "image" || work.Selected == 0 && hasImageResult);

            var status = complete ? "completed" : failed ? "failed"
                : stale ? "stalled" : hasClaim ? "processing" : "queued";
            var apiPhase = complete ? "result_saved" : failed ? "failed"
                : hasClaim ? "claimed_outcome_unconfirmed" : "not_dispatched";
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
                ProviderId = stageMatches ? work.AiProviderId : string.Empty,
                PromptVersion = stageMatches ? work.AiPromptVersion : 0,
                ImageAttempts = work.ImageAttempts,
                Note = failed || stale ? work.Note : string.Empty,
                CanReview = canReview,
                IsLegacyGo = !complete && !failed && !hasDotnetQueueMarker
            });
        }

        return tasks;
    }
}
