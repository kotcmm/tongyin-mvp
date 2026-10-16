using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Ai;

/// <summary>
/// 管理员确认图片生成失败的请求，只接受当前任务编号和人工核实依据。
/// </summary>
public sealed class AdminImageFailureReviewRequest
{
    /// <summary>需处理的生成任务编号。</summary>
    [JsonPropertyName("generation_id")]
    public string? GenerationId { get; init; }

    /// <summary>人工核实失败的依据，只记录在管理员审计中。</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
