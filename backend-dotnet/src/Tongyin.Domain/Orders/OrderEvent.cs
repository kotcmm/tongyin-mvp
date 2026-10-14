using System.Text;
namespace Tongyin.Domain.Orders;

/// <summary>
/// 订单操作历史记录。
/// </summary>
public sealed record OrderEvent(string At,string Actor,string Action,string Note="");
