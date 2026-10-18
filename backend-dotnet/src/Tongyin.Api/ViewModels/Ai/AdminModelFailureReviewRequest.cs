using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Ai;

/// <summary>管理员确认超时模型任务失败的请求 DTO（数据传输对象）。</summary>
public sealed class AdminModelFailureReviewRequest
{
    /// <summary>当前模型任务编号。</summary>
    [JsonPropertyName("generation_id")]
    public string? GenerationId { get; init; }

    /// <summary>人工核实失败的审计依据。</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
