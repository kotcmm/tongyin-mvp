using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
/// <summary>
/// 物流发货和轨迹查询管理接口。
/// </summary>
[ApiController]
public sealed class LogisticsController(LogisticsService service) : ControllerBase
{
    /// <summary>
    /// 登记订单发货信息。
    /// </summary>
    [HttpPost("/api/admin/orders/{id}/shipment")]
    public Task<IResult> CreateShipment([FromRoute] string id) => service.CreateShipment(id, HttpContext);

    /// <summary>
    /// 手动追加或更新物流轨迹。
    /// </summary>
    [HttpPost("/api/admin/orders/{id}/tracking")]
    public Task<IResult> AddTracking([FromRoute] string id) => service.AddTracking(id, HttpContext);

    /// <summary>
    /// 从快递服务刷新物流追踪信息。
    /// </summary>
    [HttpPost("/api/admin/orders/{id}/tracking/refresh")]
    public Task<IResult> RefreshTracking([FromRoute] string id) => service.RefreshTracking(id, HttpContext);
}
