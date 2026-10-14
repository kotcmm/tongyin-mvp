using System.Text;
namespace Tongyin.Domain.Orders;

/// <summary>
/// 订单操作历史记录。
/// </summary>
/// <param name="At">业务参数</param>
/// <param name="Actor">业务参数</param>
/// <param name="Action">业务参数</param>
public sealed record OrderEvent(string At,string Actor,string Action,string Note="");
