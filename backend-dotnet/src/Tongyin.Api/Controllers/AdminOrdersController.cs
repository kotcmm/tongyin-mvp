using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Api.ViewModels.Checkout;
using Tongyin.Application.Checkout;

namespace Tongyin.Api.Controllers;

/// <summary>运营人员订单制作与实际发货入口，禁止微信普通用户使用。</summary>
[ApiController]
[Route("api/admin/orders")]
[AdminAuthorize]
public sealed class AdminOrdersController(OrderFulfillmentService fulfillment) : ControllerBase
{
    /// <summary>返回运营订单列表，不向响应输出微信用户 OpenID。</summary>
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
        => Ok(await fulfillment.ListAsync(cancellationToken));

    /// <summary>只有收到微信确认付款且没有退款申请的订单允许进入制作。</summary>
    [HttpPost("{id}/status")]
    public async Task<IActionResult> ChangeStatusAsync([FromRoute] string id, [FromBody] AdminOrderStatusRequest request, CancellationToken cancellationToken)
    {
        if (request.Status != "printing")
        {
            return BadRequest(new { error = "当前仅支持开始制作，请通过实际发货入口设置已发货" });
        }
        try
        {
            var result = await fulfillment.StartPrintingAsync(id, Actor(), cancellationToken);
            return result is null ? NotFound(new { error = "订单不存在" }) : Ok(new { status = "ok" });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    /// <summary>只有制作中且没有退款请求的订单允许登记已核实的真实运单。</summary>
    [HttpPost("{id}/shipment")]
    public async Task<IActionResult> ShipAsync([FromRoute] string id, [FromBody] AdminShipmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await fulfillment.ShipAsync(id, Actor(), request.Carrier, request.CarrierCode, request.Tracking, cancellationToken);
            return result is null ? NotFound(new { error = "订单不存在" }) : Ok(new { status = "shipped" });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    private string Actor() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin-token";
}
