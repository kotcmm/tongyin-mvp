using System.Text;
namespace Tongyin.Domain.Orders;

/// <summary>
/// 订单状态快照，保存聚合持久化所需的数据。
/// </summary>
public sealed record OrderState(string Id,string Owner,string WorkId,string CreatedAt,
    ShippingAddress Recipient,int PriceFen,OrderStatus Status,
    string PaymentStartedAt,string CancelReason,IReadOnlyList<OrderEvent> History);
