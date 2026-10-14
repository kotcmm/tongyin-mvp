using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
/// <summary>
/// 后台运营写操作接口，负责账号、作品与订单状态变更。
/// </summary>
[ApiController]
public sealed class AdminOperationsController(AdminCommandService service) : ControllerBase
{
    /// <summary>
    /// 查询后台账号列表。
    /// </summary>
    [HttpGet("/api/admin/accounts")]
    public Task<IResult> ListAccounts() => service.ListAccounts(HttpContext);

    /// <summary>
    /// 创建后台人员账号。
    /// </summary>
    [HttpPost("/api/admin/accounts")]
    public Task<IResult> CreateAccount() => service.CreateAccount(HttpContext);

    /// <summary>
    /// 修改后台账号信息。
    /// </summary>
    [HttpPut("/api/admin/accounts/{id}")]
    public Task<IResult> UpdateAccount([FromRoute] string id) => service.UpdateAccount(id, HttpContext);

    /// <summary>
    /// 上传作品的图片或三维模型文件。
    /// </summary>
    [HttpPost("/api/admin/works/{id}/asset")]
    public Task<IResult> UploadWorkAsset([FromRoute] string id) => service.UploadWorkAsset(id, HttpContext);

    /// <summary>
    /// 审核并更新作品处理状态。
    /// </summary>
    [HttpPost("/api/admin/works/{id}/status")]
    public Task<IResult> UpdateWorkStatus([FromRoute] string id) => service.UpdateWorkStatus(id, HttpContext);

    /// <summary>
    /// 更新订单生产流程状态。
    /// </summary>
    [HttpPost("/api/admin/orders/{id}/status")]
    public Task<IResult> UpdateOrderStatus([FromRoute] string id) => service.UpdateOrderStatus(id, HttpContext);

    /// <summary>
    /// 申请对指定订单退款。
    /// </summary>
    [HttpPost("/api/admin/orders/{id}/refund")]
    public Task<IResult> RefundOrder([FromRoute] string id) => service.RefundOrder(id, HttpContext);
}
