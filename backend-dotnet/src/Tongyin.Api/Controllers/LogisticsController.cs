using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
[ApiController]
public sealed class LogisticsController(LogisticsService service) : ControllerBase
{
    [HttpPost("/api/admin/orders/{id}/shipment")]
    public Task<IResult> CreateShipment([FromRoute] string id) => service.CreateShipment(id, HttpContext);

    [HttpPost("/api/admin/orders/{id}/tracking")]
    public Task<IResult> AddTracking([FromRoute] string id) => service.AddTracking(id, HttpContext);

    [HttpPost("/api/admin/orders/{id}/tracking/refresh")]
    public Task<IResult> RefreshTracking([FromRoute] string id) => service.RefreshTracking(id, HttpContext);
}
