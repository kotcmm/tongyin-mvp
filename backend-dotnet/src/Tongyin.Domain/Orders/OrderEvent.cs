using System.Text;
namespace Tongyin.Domain.Orders;

/// <summary>
/// 订单操作历史记录。
/// </summary>
/// <param name="At">事件发生时间</param>
/// <param name="Actor">事件操作人</param>
/// <param name="Action">事件业务动作</param>
/// <param name="Note">附加操作说明</param>
public sealed record OrderEvent(string At,string Actor,string Action,string Note="");
