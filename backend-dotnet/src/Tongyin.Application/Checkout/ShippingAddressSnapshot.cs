using System.Text.Json.Serialization;

namespace Tongyin.Application.Checkout;

/// <summary>返回小程序的收货地址快照；不会将用户 OpenID 返回客户端。</summary>
public sealed class ShippingAddressSnapshot
{
    /// <summary>地址更新时间。</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = "";

    /// <summary>收货人。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>收货联系电话。</summary>
    [JsonPropertyName("phone")]
    public string Phone { get; init; } = "";

    /// <summary>收货详细地址。</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = "";
}
