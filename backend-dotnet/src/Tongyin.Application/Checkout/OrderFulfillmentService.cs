using Tongyin.Domain.Checkout;

namespace Tongyin.Application.Checkout;

/// <summary>管理员订单制作、发货与顾客物流查询业务入口。</summary>
public sealed class OrderFulfillmentService(IOrderFulfillmentRepository repository)
{
    /// <summary>取得运营订单队列。</summary>
    public Task<IReadOnlyList<OrderSnapshot>> ListAsync(CancellationToken cancellationToken = default)
        => repository.ListAsync(cancellationToken);

    /// <summary>管理员在微信支付成功后安排生产。</summary>
    public Task<OrderSnapshot?> StartPrintingAsync(string id, string actor, CancellationToken cancellationToken = default)
        => repository.StartPrintingAsync(id, actor, cancellationToken);

    /// <summary>管理员录入核实过的快递与运单号。</summary>
    public Task<OrderSnapshot?> ShipAsync(string id, string actor, string carrier, string carrierCode, string tracking, CancellationToken cancellationToken = default)
        => repository.ShipAsync(id, actor, ShipmentDetails.Create(carrier, carrierCode, tracking), cancellationToken);

    /// <summary>顾客只能查看本人的发货物流。</summary>
    public Task<OrderTrackingSnapshot?> GetTrackingAsync(string owner, string id, CancellationToken cancellationToken = default)
        => repository.GetTrackingAsync(owner, id, cancellationToken);
}
