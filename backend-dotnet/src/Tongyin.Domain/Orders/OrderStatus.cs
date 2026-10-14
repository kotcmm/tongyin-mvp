using System.Text;
namespace Tongyin.Domain.Orders;

/// <summary>
/// 订单生命周期状态。
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// 等待支付。
    /// </summary>
    AwaitingPayment,
    /// <summary>
    /// 支付完成。
    /// </summary>
    Paid,
    /// <summary>
    /// 正在打印制作。
    /// </summary>
    Printing,
    /// <summary>
    /// 已经发货。
    /// </summary>
    Shipped,
    /// <summary>
    /// 订单已完成。
    /// </summary>
    Completed,
    /// <summary>
    /// 订单已取消。
    /// </summary>
    Canceled,
    /// <summary>
    /// 未知订单状态。
    /// </summary>
    Unknown
}
