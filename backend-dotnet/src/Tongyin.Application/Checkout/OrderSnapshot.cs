using System.Text.Json.Serialization;

namespace Tongyin.Application.Checkout;

/// <summary>跨数据库的订单只读业务快照，保留原 Go 微信小程序使用的 JSON 字段，不输出 OpenID。</summary>
public sealed class OrderSnapshot
{
    /// <summary>订单标识。</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>关联作品标识。</summary>
    [JsonPropertyName("work_id")]
    public string WorkId { get; init; } = "";

    /// <summary>下单时间。</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = "";

    /// <summary>订单收货人。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>收货联系电话。</summary>
    [JsonPropertyName("phone")]
    public string Phone { get; init; } = "";

    /// <summary>订单级收货地址快照。</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = "";

    /// <summary>创建时冻结的价格，单位为分。</summary>
    [JsonPropertyName("price_fen")]
    public int PriceFen { get; init; }

    /// <summary>订单状态。</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    /// <summary>发起微信支付的时间。</summary>
    [JsonPropertyName("payment_started_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? PaymentStartedAt { get; init; }

    /// <summary>客户取消原因。</summary>
    [JsonPropertyName("cancel_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? CancelReason { get; init; }

    /// <summary>物流单号。</summary>
    [JsonPropertyName("tracking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Tracking { get; init; }

    /// <summary>快递公司。</summary>
    [JsonPropertyName("carrier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Carrier { get; init; }

    /// <summary>快递公司编码。</summary>
    [JsonPropertyName("carrier_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? CarrierCode { get; init; }

    /// <summary>实际发货时间。</summary>
    [JsonPropertyName("shipped_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ShippedAt { get; init; }

    /// <summary>订单完成时间。</summary>
    [JsonPropertyName("delivered_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? DeliveredAt { get; init; }

    /// <summary>最近查询物流的时间。</summary>
    [JsonPropertyName("tracking_checked_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? TrackingCheckedAt { get; init; }

    /// <summary>物流服务商提供的当前状态。</summary>
    [JsonPropertyName("tracking_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? TrackingState { get; init; }

    /// <summary>物流轨迹。</summary>
    [JsonPropertyName("tracking_events")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TrackingEventSnapshot>? TrackingEvents { get; init; }

    /// <summary>订单业务操作记录。</summary>
    [JsonPropertyName("history")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OrderHistorySnapshot>? History { get; init; }

    /// <summary>制作备注。</summary>
    [JsonPropertyName("production_note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ProductionNote { get; init; }

    /// <summary>退款处理状态。</summary>
    [JsonPropertyName("refund_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? RefundStatus { get; init; }

    /// <summary>旧 Go 内部用户标识；存在时可兼容历史后台数据。</summary>
    [JsonPropertyName("owner_user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? OwnerUserId { get; init; }
}
