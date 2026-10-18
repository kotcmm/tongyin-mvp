using Tongyin.Domain.Checkout;

namespace Tongyin.Application.Checkout;

/// <summary>运营履约持久化端口，所有变更在现有全局订单事务锁内完成。</summary>
public interface IOrderFulfillmentRepository
{
    /// <summary>读取全部订单，供受保护的运营入口查看生产和发货队列。</summary>
    Task<IReadOnlyList<OrderSnapshot>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>原子核实支付与退款状态后进入制作，并写入操作审计。</summary>
    Task<OrderSnapshot?> StartPrintingAsync(string id, string actor, CancellationToken cancellationToken = default);

    /// <summary>原子核实制作中状态、保存运单并写入操作审计。</summary>
    Task<OrderSnapshot?> ShipAsync(string id, string actor, ShipmentDetails details, CancellationToken cancellationToken = default);

    /// <summary>按微信用户归属查询真实物流，不返回其他用户订单。</summary>
    Task<OrderTrackingSnapshot?> GetTrackingAsync(string owner, string id, CancellationToken cancellationToken = default);
}
