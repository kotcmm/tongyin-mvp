using System.Text;
namespace Tongyin.Domain.Orders;

/// <summary>
/// 订单状态快照，保存聚合持久化所需的数据。
/// </summary>
/// <param name="Id">订单编号</param>
/// <param name="Owner">订单所属客户</param>
/// <param name="WorkId">关联作品编号</param>
/// <param name="CreatedAt">订单创建时间</param>
/// <param name="Recipient">收件人和收货地址</param>
/// <param name="PriceFen">订单金额，单位为分</param>
/// <param name="Status">订单业务状态</param>
/// <param name="PaymentStartedAt">开始支付的时间</param>
/// <param name="CancelReason">取消订单原因</param>
/// <param name="History">订单历史记录</param>
public sealed record OrderState(string Id,string Owner,string WorkId,string CreatedAt, ShippingAddress Recipient,int PriceFen,OrderStatus Status, string PaymentStartedAt,string CancelReason,IReadOnlyList<OrderEvent> History);
