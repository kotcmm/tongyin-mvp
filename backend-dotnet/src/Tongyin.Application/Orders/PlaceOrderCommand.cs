using Tongyin.Domain.Orders;

namespace Tongyin.Application.Orders;

/// <summary>
/// 创建订单命令，包含下单人、作品、地址及金额。
/// </summary>
/// <param name="Owner">业务参数</param>
/// <param name="WorkId">业务参数</param>
/// <param name="Name">业务参数</param>
/// <param name="Phone">业务参数</param>
/// <param name="Address">业务参数</param>
/// <param name="PriceFen">业务参数</param>
public sealed record PlaceOrderCommand(string Owner,string WorkId,string Name,string Phone,string Address,int PriceFen);
