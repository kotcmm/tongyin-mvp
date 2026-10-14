using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
[ApiController]
public sealed class AdminOperationsController(AdminCommandService service) : ControllerBase
{
    [HttpGet("/api/admin/accounts")]
    public Task<IResult> ListAccounts() => service.ListAccounts(HttpContext);

    [HttpPost("/api/admin/accounts")]
    public Task<IResult> CreateAccount() => service.CreateAccount(HttpContext);

    [HttpPut("/api/admin/accounts/{id}")]
    public Task<IResult> UpdateAccount([FromRoute] string id) => service.UpdateAccount(id, HttpContext);

    [HttpPost("/api/admin/works/{id}/asset")]
    public Task<IResult> UploadWorkAsset([FromRoute] string id) => service.UploadWorkAsset(id, HttpContext);

    [HttpPost("/api/admin/works/{id}/status")]
    public Task<IResult> UpdateWorkStatus([FromRoute] string id) => service.UpdateWorkStatus(id, HttpContext);

    [HttpPost("/api/admin/orders/{id}/status")]
    public Task<IResult> UpdateOrderStatus([FromRoute] string id) => service.UpdateOrderStatus(id, HttpContext);

    [HttpPost("/api/admin/orders/{id}/refund")]
    public Task<IResult> RefundOrder([FromRoute] string id) => service.RefundOrder(id, HttpContext);
}
