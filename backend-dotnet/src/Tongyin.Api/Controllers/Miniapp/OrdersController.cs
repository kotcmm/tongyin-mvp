using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Api.ViewModels.Checkout;
using Tongyin.Application.Checkout;

namespace Tongyin.Api.Controllers.Miniapp;

/// <summary>微信用户待付款订单创建、本人查询、收货地址修改及安全取消。</summary>
[ApiController]
[Route("api/orders")]
[WechatAuthorize]
public sealed class OrdersController(CheckoutService checkout, MiniappBusinessSettings settings) : ControllerBase
{
    /// <summary>只有模型已就绪且该作品没有有效订单时才能创建待付款订单。</summary>
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await checkout.CreateOrderAsync(User.GetWechatOpenId(), request.WorkId,
                request.Name, request.Phone, request.Address, settings.PriceFen, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, result);
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

    /// <summary>按时间倒序返回本人所有订单，不能查看其他用户的收货信息。</summary>
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
        => Ok(await checkout.ListOrdersAsync(User.GetWechatOpenId(), cancellationToken));

    /// <summary>获取当前微信用户自己的订单详情。</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAsync([FromRoute] string id, CancellationToken cancellationToken)
    {
        var order = await checkout.GetOrderAsync(User.GetWechatOpenId(), id, cancellationToken);
        return order is null ? NotFound(new { error = "订单不存在" }) : Ok(order);
    }

    /// <summary>仅更新本人待付款订单收货信息，不能通过此接口修改金额和支付状态。</summary>
    [HttpPut("{id}/shipping-address")]
    public async Task<IActionResult> UpdateShippingAsync([FromRoute] string id, [FromBody] ShippingRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await checkout.UpdateOrderShippingAsync(
                User.GetWechatOpenId(), id, request.Name, request.Phone, request.Address, cancellationToken);
            return updated is null ? NotFound(new { error = "订单不存在" }) : Ok(updated);
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

    /// <summary>取消本人未发起微信支付的待付款订单，不假称实际退款已完成。</summary>
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelAsync([FromRoute] string id, [FromBody] CancelOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await checkout.CancelOrderAsync(User.GetWechatOpenId(), id, request.Reason, cancellationToken);
            return updated is null ? NotFound(new { error = "订单不存在" }) : Ok(new { status = updated.Status });
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
}
