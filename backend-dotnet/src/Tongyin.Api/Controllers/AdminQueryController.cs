using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
/// <summary>
/// 后台管理查询接口，提供看板、搜索和账号信息。
/// </summary>
[ApiController]
public sealed class AdminQueryController(AdminQueryService service) : ControllerBase
{
    /// <summary>
    /// 验证后台管理员并签发访问令牌。
    /// </summary>
    [HttpPost("/api/admin/auth/login")]
    public Task<IResult> LoginAdmin() => service.LoginAdmin(HttpContext);

    /// <summary>
    /// 查询当前后台管理员信息。
    /// </summary>
    [HttpGet("/api/admin/me")]
    public Task<IResult> GetCurrentAdmin() => service.GetCurrentAdmin(HttpContext);

    /// <summary>
    /// 获取后台运营概览数据。
    /// </summary>
    [HttpGet("/api/admin/dashboard")]
    public Task<IResult> GetDashboard() => service.GetDashboard(HttpContext);

    /// <summary>
    /// 分页或按当前用户查询作品。
    /// </summary>
    [HttpGet("/api/admin/works")]
    public Task<IResult> ListWorks() => service.ListWorks(HttpContext);

    /// <summary>
    /// 查询订单列表。
    /// </summary>
    [HttpGet("/api/admin/orders")]
    public Task<IResult> ListOrders() => service.ListOrders(HttpContext);

    /// <summary>
    /// 查询用户列表。
    /// </summary>
    [HttpGet("/api/admin/users")]
    public Task<IResult> ListUsers() => service.ListUsers(HttpContext);

    /// <summary>
    /// 查询后台操作审计记录。
    /// </summary>
    [HttpGet("/api/admin/audit")]
    public Task<IResult> ListAudit() => service.ListAudit(HttpContext);

    /// <summary>
    /// 按筛选条件搜索作品。
    /// </summary>
    [HttpGet("/api/admin/search/works")]
    public Task<IResult> SearchWorks() => service.SearchWorks(HttpContext);

    /// <summary>
    /// 按筛选条件搜索订单。
    /// </summary>
    [HttpGet("/api/admin/search/orders")]
    public Task<IResult> SearchOrders() => service.SearchOrders(HttpContext);

    /// <summary>
    /// 按筛选条件搜索用户。
    /// </summary>
    [HttpGet("/api/admin/search/users")]
    public Task<IResult> SearchUsers() => service.SearchUsers(HttpContext);

    /// <summary>
    /// 启用或停用指定用户。
    /// </summary>
    [HttpPost("/api/admin/users/{id}/status")]
    public Task<IResult> ChangeUserStatus([FromRoute] string id) => service.ChangeUserStatus(id, HttpContext);
}
