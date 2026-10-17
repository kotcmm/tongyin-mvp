using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Checkout;

/// <summary>小程序提交新订单的请求参数；价格和状态均由服务器确定，不接受客户端报价。</summary>
public sealed class CreateOrderRequest
{
    /// <summary>已完成模型生成的作品标识。</summary>
    [JsonPropertyName("work_id")]
    public required string WorkId { get; init; }

    /// <summary>收货人。</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>收货电话。</summary>
    [JsonPropertyName("phone")]
    public required string Phone { get; init; }

    /// <summary>详细地址。</summary>
    [JsonPropertyName("address")]
    public required string Address { get; init; }
}
