using System.Text;
namespace Tongyin.Domain.Orders;

/// <summary>
/// 订单状态快照，保存聚合持久化所需的数据。
/// </summary>
/// <param name="Id">业务参数</param>
/// <param name="Owner">业务参数</param>
/// <param name="WorkId">业务参数</param>
/// <param name="CreatedAt">业务参数</param>
/// <param name="Recipient">业务参数</param>
/// <param name="PriceFen">业务参数</param>
/// <param name="Status">业务参数</param>
/// <param name="PaymentStartedAt">业务参数</param>
/// <param name="CancelReason">业务参数</param>
/// <param name="History">业务参数</param>
public sealed record OrderState(string Id,string Owner,string WorkId,string CreatedAt,
    ShippingAddress Recipient,int PriceFen,OrderStatus Status,
    string PaymentStartedAt,string CancelReason,IReadOnlyList<OrderEvent> History);
