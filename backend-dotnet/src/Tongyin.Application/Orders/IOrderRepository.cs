using Tongyin.Domain.Orders;

namespace Tongyin.Application.Orders;

/// <summary>
/// 订单仓储接口，声明原子下单和订单更新操作。
/// </summary>
public interface IOrderRepository
{
    // Creates order and latest address in a single atomic transaction.
    // Duplicate/ready-work checks execute INSIDE that transaction.
    /// <summary>
    /// 执行下单用例并保存订单。
    /// </summary>
    Task<Order> PlaceAsync(Order order,CancellationToken ct=default);
    /// <summary>
    /// 按照所属用户查找业务对象。
    /// </summary>
    Task<Order?> FindAsync(string owner,string id,CancellationToken ct=default);
    /// <summary>
    /// 在原子事务中加载并修改聚合。
    /// </summary>
    Task<Order> UpdateAsync(string owner,string id,Action<Order> change,CancellationToken ct=default);
}
