using Tongyin.Domain.Orders;

namespace Tongyin.Application.Orders;

public interface IOrderRepository
{
    // Creates order and latest address in a single atomic transaction.
    // Duplicate/ready-work checks execute INSIDE that transaction.
    Task<Order> PlaceAsync(Order order,CancellationToken ct=default);
    Task<Order?> FindAsync(string owner,string id,CancellationToken ct=default);
    Task<Order> UpdateAsync(string owner,string id,Action<Order> change,CancellationToken ct=default);
}

public interface IOrderIdsAndTime
{
    string NewId();
    string Now();
}
