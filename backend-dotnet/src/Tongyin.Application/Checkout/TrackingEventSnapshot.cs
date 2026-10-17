using System.Text.Json.Serialization;

namespace Tongyin.Application.Checkout;

/// <summary>旧 Go 物流轨迹事件格式。</summary>
public sealed class TrackingEventSnapshot
{
    /// <summary>轨迹更新时间。</summary>
    [JsonPropertyName("time")]
    public string Time { get; init; } = "";

    /// <summary>运输节点描述。</summary>
    [JsonPropertyName("context")]
    public string Context { get; init; } = "";

    /// <summary>物流事件数据来源。</summary>
    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Source { get; init; }
}
