using System.Text.Json.Serialization;

namespace Tongyin.Application.Works;

/// <summary>后台图片/3D 生成任务的当前可观测状态，不包含原画 URL、用户 OpenID 或 API Key。</summary>
public sealed class AiGenerationTask
{
    /// <summary>作品标识。</summary>
    [JsonPropertyName("work_id")]
    public required string WorkId { get; init; }

    /// <summary>当前阶段的任务编号；历史阶段无法确认时为空。</summary>
    [JsonPropertyName("generation_id")]
    public required string GenerationId { get; init; }

    /// <summary>image 或 model。</summary>
    [JsonPropertyName("stage")]
    public required string Stage { get; init; }

    /// <summary>queued / processing / stalled / completed / failed。</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>API 状态，仅代表系统已记录的事件，不推测服务商真实执行百分比。</summary>
    [JsonPropertyName("api_phase")]
    public required string ApiPhase { get; init; }

    /// <summary>作品创建时间；不代表图片或 3D 阶段的具体申请时间。</summary>
    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    /// <summary>本阶段被原子认领的时间；未认领或历史阶段为空。</summary>
    [JsonPropertyName("started_at")]
    public required string StartedAt { get; init; }

    /// <summary>本阶段记录的服务商编号，未保存则留空。</summary>
    [JsonPropertyName("provider_id")]
    public required string ProviderId { get; init; }

    /// <summary>本阶段记录的提示词版本，缺失时为零。</summary>
    [JsonPropertyName("prompt_version")]
    public required int PromptVersion { get; init; }

    /// <summary>图片已使用的次数；不等于真实服务商计费次数。</summary>
    [JsonPropertyName("image_attempts")]
    public required int ImageAttempts { get; init; }

    /// <summary>任务当前错误或人工核查备注，原始 HTTP 密钥不得写入。</summary>
    [JsonPropertyName("note")]
    public required string Note { get; init; }

    /// <summary>仅对超过 15 分钟且符合现有人工核查条件的任务显示人工恢复操作。</summary>
    [JsonPropertyName("can_review")]
    public required bool CanReview { get; init; }

    /// <summary>混元官方任务编号；未实际提交成功时为空。</summary>
    [JsonPropertyName("provider_job_id")]
    public required string ProviderJobId { get; init; }

    /// <summary>仅为兼容现有前端列表接口而保留，当前不再支持 Go 任务适配。</summary>
    [JsonPropertyName("is_legacy_go")]
    public required bool IsLegacyGo { get; init; }
}
