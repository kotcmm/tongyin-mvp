using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Application.Checkout;

namespace Tongyin.Api.Controllers.Miniapp;

/// <summary>微信用户本人已发货订单的物流查询入口。</summary>
[ApiController]
[Route("api/orders")]
[WechatAuthorize]
public sealed class OrderTrackingController(OrderFulfillmentService fulfillment) : ControllerBase
{
    /// <summary>仅返回本人真实运单和已记录轨迹；不臆造运输状态。</summary>
    [HttpGet("{id}/tracking")]
    public async Task<IActionResult> GetAsync([FromRoute] string id, CancellationToken cancellationToken)
    {
        try
        {
            var tracking = await fulfillment.GetTrackingAsync(User.GetWechatOpenId(), id, cancellationToken);
            return tracking is null ? NotFound(new { error = "订单不存在" }) : Ok(tracking);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}
