using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Checkout;

/// <summary>管理员登记实际物流的强类型请求。</summary>
public sealed class AdminShipmentRequest
{
    /// <summary>物流公司名称。</summary>
    [JsonPropertyName("carrier")]
    public required string Carrier { get; init; }

    /// <summary>快递100物流公司编码。</summary>
    [JsonPropertyName("carrier_code")]
    public required string CarrierCode { get; init; }

    /// <summary>实际运单编号。</summary>
    [JsonPropertyName("tracking")]
    public required string Tracking { get; init; }
}
