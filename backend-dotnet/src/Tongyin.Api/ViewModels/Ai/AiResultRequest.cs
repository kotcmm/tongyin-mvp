using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Ai;

/// <summary>兼容 Go worker 原有 POST /api/internal/ai-result 回调字段。</summary>
public sealed class AiResultRequest
{
    /// <summary>回调关联的作品编号。</summary>
    [JsonPropertyName("work_id")]
    public string WorkId { get; init; } = string.Empty;

    /// <summary>生成阶段；当前 .NET 回调支持 model。</summary>
    [JsonPropertyName("stage")]
    public string Stage { get; init; } = string.Empty;

    /// <summary>服务端生成任务版本号。</summary>
    [JsonPropertyName("generation_id")]
    public string GenerationId { get; init; } = string.Empty;

    /// <summary>模型生成结果的公网 HTTPS 地址。</summary>
    [JsonPropertyName("model_url")]
    public string ModelUrl { get; init; } = string.Empty;

    /// <summary>服务商明确报告的错误信息。</summary>
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}
