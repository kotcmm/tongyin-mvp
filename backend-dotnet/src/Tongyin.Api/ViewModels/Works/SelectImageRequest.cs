using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Works;

/// <summary>小程序确认卡通图的请求参数，仅接受显式传入的整数索引。</summary>
public sealed class SelectImageRequest
{
    /// <summary>要确认的卡通图序号；单图 MVP 只允许 0，具体业务约束由领域层验证。</summary>
    [JsonPropertyName("index")]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public required int Index { get; init; }
}
