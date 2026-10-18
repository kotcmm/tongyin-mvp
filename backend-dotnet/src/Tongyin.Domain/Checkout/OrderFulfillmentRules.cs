namespace Tongyin.Domain.Checkout;

/// <summary>订单从收款到制作、发货的严格状态迁移规则。</summary>
public static class OrderFulfillmentRules
{
    /// <summary>只允许已经由可信支付回调确认且没有退款申请的订单进入制作。</summary>
    public static void RequireStartPrinting(string status, string? refundStatus)
    {
        if (status != "paid")
        {
            throw new InvalidOperationException("只有已付款订单可以开始制作");
        }
        if (!string.IsNullOrWhiteSpace(refundStatus))
        {
            throw new InvalidOperationException("订单存在退款申请，不能进入生产");
        }
    }

    /// <summary>只允许没有退款申请的制作中订单登记实际发货。</summary>
    public static void RequireShipment(string status, string? refundStatus)
    {
        if (status != "printing")
        {
            throw new InvalidOperationException("必须在制作中状态完成打包后才能发货");
        }
        if (!string.IsNullOrWhiteSpace(refundStatus))
        {
            throw new InvalidOperationException("订单存在退款申请，不能发货");
        }
    }

    /// <summary>只有已发货或已完成订单可向顾客展示物流。</summary>
    public static void RequireTrackingAvailable(string status)
    {
        if (status is not ("shipped" or "completed"))
        {
            throw new InvalidOperationException("订单尚未发货");
        }
    }
}
