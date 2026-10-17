using Tongyin.Domain.Checkout;

namespace Tongyin.Application.Checkout;

/// <summary>订单与地址持久化端口；写操作使用同一数据库事务实现唯一性和状态检查。</summary>
public interface ICheckoutRepository
{
    /// <summary>读取当前默认地址，若历史 Go 订单存在则允许回填最近下单地址。</summary>
    Task<ShippingAddressSnapshot?> GetShippingAsync(string owner, CancellationToken cancellationToken = default);

    /// <summary>保存本人唯一默认收货地址。</summary>
    Task<ShippingAddressSnapshot> SaveShippingAsync(string owner, ShippingDetails details, CancellationToken cancellationToken = default);

    /// <summary>原子检查模型状态、禁止重复有效订单、创建待付款订单并保存最新地址。</summary>
    Task<OrderSnapshot> CreateOrderAsync(string owner, string workId, ShippingDetails details, int priceFen, CancellationToken cancellationToken = default);

    /// <summary>按用户范围读取订单，按时间从新到旧排序。</summary>
    Task<IReadOnlyList<OrderSnapshot>> ListOrdersAsync(string owner, CancellationToken cancellationToken = default);

    /// <summary>仅允许本人查询订单详情。</summary>
    Task<OrderSnapshot?> GetOrderAsync(string owner, string id, CancellationToken cancellationToken = default);

    /// <summary>查询本人作品当前最新未取消订单，没有时返回空。</summary>
    Task<OrderSnapshot?> GetWorkOrderAsync(string owner, string workId, CancellationToken cancellationToken = default);

    /// <summary>只允许修改本人待付款订单的收货快照。</summary>
    Task<OrderSnapshot?> UpdateOrderShippingAsync(string owner, string id, ShippingDetails details, CancellationToken cancellationToken = default);

    /// <summary>只能取消未启动支付的本人待付款订单。</summary>
    Task<OrderSnapshot?> CancelOrderAsync(string owner, string id, string reason, CancellationToken cancellationToken = default);
}
