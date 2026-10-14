using System.Text;
namespace Tongyin.Domain.Orders;

/// <summary>
/// 订单生命周期状态。
/// </summary>
public enum OrderStatus { AwaitingPayment, Paid, Printing, Shipped, Completed, Canceled, Unknown }
