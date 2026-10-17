namespace Tongyin.Domain.Checkout;

/// <summary>童印订单的最小领域实体；新建时只产生待付款订单，不触发实际微信支付。</summary>
public sealed class Order
{
    private Order(string id, string owner, string workId, ShippingDetails shipping, int priceFen, DateTimeOffset createdAt)
    {
        Id = id;
        Owner = owner;
        WorkId = workId;
        Shipping = shipping;
        PriceFen = priceFen;
        CreatedAt = createdAt;
    }

    /// <summary>订单标识。</summary>
    public string Id { get; }

    /// <summary>微信用户标识，只用于服务端权限判断。</summary>
    public string Owner { get; }

    /// <summary>制作模型所对应作品。</summary>
    public string WorkId { get; }

    /// <summary>下单时确认的收货信息快照。</summary>
    public ShippingDetails Shipping { get; }

    /// <summary>服务器确认的最终订单金额，以分为单位。</summary>
    public int PriceFen { get; }

    /// <summary>订单创建时间。</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>当前订单状态，创建时必须为待付款。</summary>
    public string Status => "awaiting_payment";

    /// <summary>仅在模型就绪且服务端价格合法时生成订单实体。</summary>
    public static Order Create(string id, string owner, string workId, bool modelReady, ShippingDetails shipping, int priceFen, DateTimeOffset createdAt)
    {
        if (!modelReady)
        {
            throw new InvalidOperationException("模型尚未准备好");
        }
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(workId) || priceFen <= 0)
        {
            throw new ArgumentException("订单信息无效");
        }
        ArgumentNullException.ThrowIfNull(shipping);
        return new Order(id, owner, workId, shipping, priceFen, createdAt);
    }

    /// <summary>仅待付款且尚未发起支付的订单可以取消；若可能已支付则拒绝。</summary>
    public static bool CanCancel(string status, string? paymentStartedAt)
    {
        if (status != "awaiting_payment")
        {
            return false;
        }
        if (!string.IsNullOrEmpty(paymentStartedAt))
        {
            throw new InvalidOperationException("该订单已发起微信支付，请等待支付状态确认后联系客服处理取消");
        }
        return true;
    }
}
