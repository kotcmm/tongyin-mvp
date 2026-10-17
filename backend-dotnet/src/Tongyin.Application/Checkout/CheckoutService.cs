using Tongyin.Domain.Checkout;

namespace Tongyin.Application.Checkout;

/// <summary>微信用户收货地址与订单业务用例，避免 Controller 直接处理数据库或金额。</summary>
public sealed class CheckoutService(ICheckoutRepository repository)
{
    /// <summary>读取当前用户的默认收货地址。</summary>
    public Task<ShippingAddressSnapshot?> GetShippingAsync(string owner, CancellationToken cancellationToken = default)
        => repository.GetShippingAsync(owner, cancellationToken);

    /// <summary>验证并保存当前用户收货地址。</summary>
    public Task<ShippingAddressSnapshot> SaveShippingAsync(string owner, string name, string phone, string address, CancellationToken cancellationToken = default)
        => repository.SaveShippingAsync(owner, ShippingDetails.Create(name, phone, address), cancellationToken);

    /// <summary>验证收货信息并以服务端价格创建待付款订单。</summary>
    public Task<OrderSnapshot> CreateOrderAsync(string owner, string workId, string name, string phone, string address, int priceFen, CancellationToken cancellationToken = default)
        => repository.CreateOrderAsync(owner, workId, ShippingDetails.Create(name, phone, address), priceFen, cancellationToken);

    /// <summary>读取本人订单列表。</summary>
    public Task<IReadOnlyList<OrderSnapshot>> ListOrdersAsync(string owner, CancellationToken cancellationToken = default)
        => repository.ListOrdersAsync(owner, cancellationToken);

    /// <summary>读取本人指定订单。</summary>
    public Task<OrderSnapshot?> GetOrderAsync(string owner, string id, CancellationToken cancellationToken = default)
        => repository.GetOrderAsync(owner, id, cancellationToken);

    /// <summary>查询指定本人作品最近的一笔未取消订单。</summary>
    public Task<OrderSnapshot?> GetWorkOrderAsync(string owner, string workId, CancellationToken cancellationToken = default)
        => repository.GetWorkOrderAsync(owner, workId, cancellationToken);

    /// <summary>更新本人待付款订单的收货信息，保存的默认地址不受影响。</summary>
    public Task<OrderSnapshot?> UpdateOrderShippingAsync(string owner, string id, string name, string phone, string address, CancellationToken cancellationToken = default)
        => repository.UpdateOrderShippingAsync(owner, id, ShippingDetails.Create(name, phone, address), cancellationToken);

    /// <summary>取消本人未支付订单，超过 200 字的原因拒绝。</summary>
    public Task<OrderSnapshot?> CancelOrderAsync(string owner, string id, string? reason, CancellationToken cancellationToken = default)
    {
        reason = reason?.Trim() ?? "";
        if (reason.EnumerateRunes().Count() > 200)
        {
            throw new ArgumentException("取消原因太长");
        }
        return repository.CancelOrderAsync(owner, id, reason, cancellationToken);
    }
}
