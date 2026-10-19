using System.Text.Json.Serialization;

namespace Tongyin.Application.Works;

/// <summary>
/// 需要管理员人工核查的图片生成任务摘要，不包含微信用户标识、密钥或图片下载地址。
/// </summary>
public sealed class StalledImageTask
{
    /// <summary>发生异常的作品编号。</summary>
    [JsonPropertyName("work_id")]
    public required string WorkId { get; init; }

    /// <summary>本次生成任务编号。</summary>
    [JsonPropertyName("generation_id")]
    public required string GenerationId { get; init; }

    /// <summary>图片执行器最初认领任务的时间。</summary>
    [JsonPropertyName("started_at")]
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>该作品已使用的图片生成次数。</summary>
    [JsonPropertyName("image_attempts")]
    public required int ImageAttempts { get; init; }

    /// <summary>已记录的图片 AI 服务商编号，可能为空。</summary>
    [JsonPropertyName("provider_id")]
    public required string ProviderId { get; init; }

}
