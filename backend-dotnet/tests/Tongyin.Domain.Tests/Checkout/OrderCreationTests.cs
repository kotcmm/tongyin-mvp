using Tongyin.Domain.Checkout;
using Xunit;

namespace Tongyin.Domain.Tests.Checkout;

/// <summary>订单只在明确模型准备好后才能进入待付款状态，不假称已支付。</summary>
public sealed class OrderCreationTests
{
    /// <summary>创建成功时生成待付款订单和明确的收货地址快照。</summary>
    [Fact]
    public void Create_ReadyModel_StartsAwaitingPayment()
    {
        var shipping = ShippingDetails.Create("张三", "13800138000", "广州市海珠区新港东路100号");
        var order = Order.Create("00112233445566778899aabb", "openid-1", "work-1", true,
            shipping, 12900, DateTimeOffset.Parse("2026-10-17T08:00:00+08:00"));
        Assert.Equal("awaiting_payment", order.Status);
        Assert.Equal("work-1", order.WorkId);
        Assert.Equal(12900, order.PriceFen);
        Assert.Equal("张三", order.Shipping.Name);
    }

    /// <summary>模型尚未生成完成时不得允许创建订单。</summary>
    [Fact]
    public void Create_PendingModel_Rejects()
    {
        var shipping = ShippingDetails.Create("张三", "13800138000", "广州市海珠区新港东路100号");
        Assert.Throws<InvalidOperationException>(() => Order.Create("id-1", "openid-1", "work-1", false,
            shipping, 12900, DateTimeOffset.UtcNow));
    }

    /// <summary>只有没有启动支付的待付款订单可以取消，防止撤销可能已付款订单。</summary>
    [Fact]
    public void Cancel_PaymentAlreadyStarted_Rejects()
    {
        var shipping = ShippingDetails.Create("张三", "13800138000", "广州市海珠区新港东路100号");
        Assert.Throws<InvalidOperationException>(() => Order.CanCancel("awaiting_payment", "2026-10-17T09:00:00+08:00"));
        Assert.True(Order.CanCancel("awaiting_payment", ""));
        Assert.False(Order.CanCancel("paid", ""));
    }
}
