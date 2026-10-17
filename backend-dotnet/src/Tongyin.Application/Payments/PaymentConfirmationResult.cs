namespace Tongyin.Application.Payments;

/// <summary>微信支付已验签成功通知的订单落库结果，用于处理重发回调和取消后到账。</summary>
public enum PaymentConfirmationResult
{
    /// <summary>首次成功确认款项。</summary>
    Paid,
    /// <summary>同一微信交易重复通知，订单未再次变更。</summary>
    Duplicate,
    /// <summary>已取消订单却收到真实款项，需要人工退款处理。</summary>
    PaidAfterCancellation
}
