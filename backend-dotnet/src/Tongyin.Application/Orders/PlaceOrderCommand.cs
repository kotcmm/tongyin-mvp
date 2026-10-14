using Tongyin.Domain.Orders;

namespace Tongyin.Application.Orders;

/// <summary>
/// 创建订单命令，包含下单人、作品、地址及金额。
/// </summary>
public sealed record PlaceOrderCommand(string Owner,string WorkId,string Name,string Phone,string Address,int PriceFen);
