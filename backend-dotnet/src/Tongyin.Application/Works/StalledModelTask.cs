using System.Text.Json.Serialization;

namespace Tongyin.Application.Works;

/// <summary>运营人员需要核查的已认领超时 3D 模型任务，不包含微信用户标识。</summary>
public sealed class StalledModelTask
{
    /// <summary>作品编号。</summary>
    [JsonPropertyName("work_id")]
    public required string WorkId { get; init; }

    /// <summary>当前模型生成任务编号。</summary>
    [JsonPropertyName("generation_id")]
    public required string GenerationId { get; init; }

    /// <summary>模型任务开始时间。</summary>
    [JsonPropertyName("started_at")]
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>用户已消耗的图片生成次数。</summary>
    [JsonPropertyName("image_attempts")]
    public required int ImageAttempts { get; init; }

    /// <summary>模型服务商标识。</summary>
    [JsonPropertyName("provider_id")]
    public required string ProviderId { get; init; }

    /// <summary>是否为不带 .NET 认领编号的旧 Go 模型任务。</summary>
    [JsonPropertyName("is_legacy_go")]
    public required bool IsLegacyGo { get; init; }
}
