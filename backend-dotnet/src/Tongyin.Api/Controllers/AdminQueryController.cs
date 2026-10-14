using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Application;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
[ApiController]
public sealed class AdminQueryController(AdminQueryService service) : ControllerBase
{
    [HttpPost("/api/admin/auth/login")]
    public Task<IResult> LoginAdmin() => service.LoginAdmin(HttpContext);

    [HttpGet("/api/admin/me")]
    public Task<IResult> GetCurrentAdmin() => service.GetCurrentAdmin(HttpContext);

    [HttpGet("/api/admin/dashboard")]
    public Task<IResult> GetDashboard() => service.GetDashboard(HttpContext);

    [HttpGet("/api/admin/works")]
    public Task<IResult> ListWorks() => service.ListWorks(HttpContext);

    [HttpGet("/api/admin/orders")]
    public Task<IResult> ListOrders() => service.ListOrders(HttpContext);

    [HttpGet("/api/admin/users")]
    public Task<IResult> ListUsers() => service.ListUsers(HttpContext);

    [HttpGet("/api/admin/audit")]
    public Task<IResult> ListAudit() => service.ListAudit(HttpContext);

    [HttpGet("/api/admin/search/works")]
    public Task<IResult> SearchWorks() => service.SearchWorks(HttpContext);

    [HttpGet("/api/admin/search/orders")]
    public Task<IResult> SearchOrders() => service.SearchOrders(HttpContext);

    [HttpGet("/api/admin/search/users")]
    public Task<IResult> SearchUsers() => service.SearchUsers(HttpContext);

    [HttpPost("/api/admin/users/{id}/status")]
    public Task<IResult> ChangeUserStatus([FromRoute] string id) => service.ChangeUserStatus(id, HttpContext);
}
