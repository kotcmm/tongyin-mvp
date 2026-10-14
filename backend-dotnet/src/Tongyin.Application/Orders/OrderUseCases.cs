using Tongyin.Domain.Orders;

namespace Tongyin.Application.Orders;

public sealed record PlaceOrderCommand(string Owner,string WorkId,string Name,string Phone,string Address,int PriceFen);

public sealed class OrderUseCases(IOrderRepository orders,IOrderIdsAndTime ids)
{
    public async Task<OrderState> PlaceAsync(PlaceOrderCommand command,CancellationToken ct=default)
    {
        var recipient=new ShippingAddress(command.Name,command.Phone,command.Address);
        var order=Order.Place(ids.NewId(),command.Owner,command.WorkId,ids.Now(),recipient,command.PriceFen);
        return (await orders.PlaceAsync(order,ct)).Snapshot();
    }

    public async Task CancelAsync(string owner,string id,string reason,CancellationToken ct=default)
    {
        await orders.UpdateAsync(owner,id,order=>order.Cancel(reason,ids.Now()),ct);
    }

    public async Task<OrderState> UpdateAddressAsync(string owner,string id,ShippingAddress address,CancellationToken ct=default)
    {
        return (await orders.UpdateAsync(owner,id,order=>order.UpdateAddress(address),ct)).Snapshot();
    }
}
