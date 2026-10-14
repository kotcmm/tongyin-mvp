using Tongyin.Domain.Orders;

namespace Tongyin.Application.Orders;

/// <summary>
/// 创建订单命令，包含下单人、作品、地址及金额。
/// </summary>
/// <param name="Owner">下单客户标识</param>
/// <param name="WorkId">关联作品标识</param>
/// <param name="Name">收件人姓名</param>
/// <param name="Phone">收件人联系电话</param>
/// <param name="Address">详细收货地址</param>
/// <param name="PriceFen">订单金额，单位为分</param>
public sealed record PlaceOrderCommand(string Owner,string WorkId,string Name,string Phone,string Address,int PriceFen);
