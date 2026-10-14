using Tongyin.Domain.Orders;

namespace Tongyin.Application.Orders;

/// <summary>
/// 订单应用用例，协调下单、取消和地址更新。
/// </summary>
public sealed class OrderUseCases(IOrderRepository orders,IOrderIdsAndTime ids)
{
    /// <summary>
    /// 执行下单用例并保存订单。
    /// </summary>
    public async Task<OrderState> PlaceAsync(PlaceOrderCommand command,CancellationToken ct=default)
    {
        var recipient=new ShippingAddress(command.Name,command.Phone,command.Address);
        var order=Order.Place(ids.NewId(),command.Owner,command.WorkId,ids.Now(),recipient,command.PriceFen);
        return (await orders.PlaceAsync(order,ct)).Snapshot();
    }

    /// <summary>
    /// 执行取消订单用例。
    /// </summary>
    public async Task CancelAsync(string owner,string id,string reason,CancellationToken ct=default)
    {
        await orders.UpdateAsync(owner,id,order=>order.Cancel(reason,ids.Now()),ct);
    }

    /// <summary>
    /// 执行修改订单收货信息用例。
    /// </summary>
    public async Task<OrderState> UpdateAddressAsync(string owner,string id,ShippingAddress address,CancellationToken ct=default)
    {
        return (await orders.UpdateAsync(owner,id,order=>order.UpdateAddress(address),ct)).Snapshot();
    }
}
