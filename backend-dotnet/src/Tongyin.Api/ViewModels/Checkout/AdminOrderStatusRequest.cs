using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Checkout;

/// <summary>管理员订单制作状态变更的强类型请求。</summary>
public sealed class AdminOrderStatusRequest
{
    /// <summary>目标订单状态，当前支持 printing。</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }
}
