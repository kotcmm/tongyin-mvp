using System.Text.Json.Serialization;

namespace Tongyin.Application.Checkout;

/// <summary>小程序订单物流查询结果，字段保持与既有 Go 接口兼容。</summary>
public sealed class OrderTrackingSnapshot
{
    /// <summary>快递公司名称。</summary>
    [JsonPropertyName("company")]
    public string Company { get; init; } = "";

    /// <summary>快递公司查询编码。</summary>
    [JsonPropertyName("company_code")]
    public string CompanyCode { get; init; } = "";

    /// <summary>实际运单号。</summary>
    [JsonPropertyName("tracking_no")]
    public string TrackingNo { get; init; } = "";

    /// <summary>物流状态；无已核实结果时为 unavailable。</summary>
    [JsonPropertyName("tracking_state")]
    public string TrackingState { get; init; } = "";

    /// <summary>最近一次物流核实时间。</summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = "";

    /// <summary>真实物流轨迹；尚未核实则是空数组。</summary>
    [JsonPropertyName("events")]
    public IReadOnlyList<TrackingEventSnapshot> Events { get; init; } = [];

    /// <summary>轨迹来源：unavailable、kuaidi100 或 manual。</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = "unavailable";
}
