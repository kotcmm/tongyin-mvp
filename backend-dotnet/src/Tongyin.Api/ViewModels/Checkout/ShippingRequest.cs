using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Checkout;

/// <summary>收货信息更新请求，使用 ASP.NET Core 强类型 JSON 模型绑定。</summary>
public sealed class ShippingRequest
{
    /// <summary>收货人姓名。</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>收货联系电话。</summary>
    [JsonPropertyName("phone")]
    public required string Phone { get; init; }

    /// <summary>收货详细地址。</summary>
    [JsonPropertyName("address")]
    public required string Address { get; init; }
}
