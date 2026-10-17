using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Checkout;

/// <summary>用户取消尚未付款订单的请求参数。</summary>
public sealed class CancelOrderRequest
{
    /// <summary>可选取消说明，最长 200 个字符。</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
