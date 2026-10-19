using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Application.Checkout;
using Tongyin.Application.Works;

namespace Tongyin.Api.Controllers.Miniapp;

/// <summary>小程序在作品预览页读取已存在订单，避免用户重复创建订单。</summary>
[ApiController]
[Route("api/works")]
[WechatAuthorize]
public sealed class WorkOrderController(GetWork getWork, CheckoutService checkout) : ControllerBase
{
    /// <summary>仅本人作品可查询当前有效订单；无订单返回 `{"order":null}`。</summary>
    [HttpGet("{id}/order")]
    public async Task<IActionResult> GetAsync([FromRoute] string id, CancellationToken cancellationToken)
    {
        var owner = User.GetWechatOpenId();
        var work = await getWork.ExecuteAsync(owner, id, cancellationToken);
        if (work is null)
        {
            return NotFound(new { error = "作品不存在" });
        }

        var order = await checkout.GetWorkOrderAsync(owner, id, cancellationToken);
        return Ok(new { order });
    }
}
