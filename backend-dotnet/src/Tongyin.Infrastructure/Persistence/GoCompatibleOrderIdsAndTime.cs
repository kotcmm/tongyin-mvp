using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Application.Orders;
using Tongyin.Domain.Orders;
using Tongyin.Domain.Works;

namespace Tongyin.Infrastructure.Persistence;

/// <summary>
/// 生成与 Go 服务兼容的订单标识及时间。
/// </summary>
public sealed class GoCompatibleOrderIdsAndTime : IOrderIdsAndTime
{
    /// <summary>
    /// 生成 Go 兼容的唯一标识。
    /// </summary>
    public string NewId()=>Doc.NewId();
    /// <summary>
    /// 获取当前时间。
    /// </summary>
    public string Now()=>Doc.Now();
}
