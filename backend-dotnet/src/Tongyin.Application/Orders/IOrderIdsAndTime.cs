using Tongyin.Domain.Orders;

namespace Tongyin.Application.Orders;

/// <summary>
/// 订单标识与时间提供接口。
/// </summary>
public interface IOrderIdsAndTime
{
    /// <summary>
    /// 生成新的业务标识。
    /// </summary>
    string NewId();
    /// <summary>
    /// 取得当前时间。
    /// </summary>
    string Now();
}
