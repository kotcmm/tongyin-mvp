using System.Text.Json.Serialization;

namespace Tongyin.Application.Works;

/// <summary>
/// 供作品查询使用的只读快照，保留 童印持久化 JSON 的业务字段。
/// </summary>
public sealed class WorkSnapshot
{
    /// <summary>作品标识。</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>创建时间文本。</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>原画媒体文件名。</summary>
    [JsonPropertyName("original")]
    public string Original { get; init; } = string.Empty;

    /// <summary>候选形象图片文件名。</summary>
    [JsonPropertyName("candidates")]
    public IReadOnlyList<string> Candidates { get; init; } = Array.Empty<string>();

    /// <summary>已选择的候选图下标。</summary>
    [JsonPropertyName("selected")]
    public int Selected { get; init; }

    /// <summary>预览图片文件名。</summary>
    [JsonPropertyName("preview")]
    public IReadOnlyList<string> Preview { get; init; } = Array.Empty<string>();

    /// <summary>模型文件名。</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    /// <summary>作品业务状态。</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>图片生成尝试次数。</summary>
    [JsonPropertyName("image_attempts")]
    public int ImageAttempts { get; init; }

    /// <summary>生成任务标识。</summary>
    [JsonPropertyName("generation_id")]
    public string GenerationId { get; init; } = string.Empty;

    /// <summary>作品备注。</summary>
    [JsonPropertyName("note")]
    public string Note { get; init; } = string.Empty;

    /// <summary>业务用户内部标识，未设置时为空。</summary>
    [JsonPropertyName("owner_user_id")]
    public string OwnerUserId { get; init; } = string.Empty;

    /// <summary>执行 AI 生成所用的服务商标识。</summary>
    [JsonPropertyName("ai_provider_id")]
    public string AiProviderId { get; init; } = string.Empty;

    /// <summary>执行时使用的提示词版本。</summary>
    [JsonPropertyName("ai_prompt_version")]
    public int AiPromptVersion { get; init; }

    /// <summary>当前 AI 任务的生成阶段。</summary>
    [JsonPropertyName("ai_task_stage")]
    public string AiTaskStage { get; init; } = string.Empty;

    /// <summary>AI 任务开始时间。</summary>
    [JsonPropertyName("ai_started_at")]
    public string AiStartedAt { get; init; } = string.Empty;

    /// <summary>当前图片生成任务持久化认领编号，同一编号只允许被认领一次。</summary>
    [JsonPropertyName("ai_execution_claim_id")]
    public string AiExecutionClaimId { get; init; } = string.Empty;
}
