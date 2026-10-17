using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Application.Payments;

namespace Tongyin.Api.Controllers;

/// <summary>微信小程序本人订单 JSAPI 预下单入口，不读取或接受客户端报价。</summary>
[ApiController]
[WechatAuthorize]
[Route("api/orders")]
public sealed class WechatPaymentController(StartWechatPayment payment, WechatPaymentAppIdResolver appIdResolver) : ControllerBase
{
    /// <summary>提交前原子记录支付尝试，之后从微信获取已签名的小程序支付参数。</summary>
    [HttpPost("{id}/pay")]
    public async Task<IActionResult> PayAsync([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (!payment.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "微信商户配置尚未完成，暂时不能付款" });
        }

        var appId = await appIdResolver.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(appId))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "微信小程序 AppID 尚未配置" });
        }

        try
        {
            var parameters = await payment.ExecuteAsync(User.GetWechatOpenId(), id, appId, cancellationToken);
            return parameters is null ? NotFound(new { error = "订单不存在" }) : Ok(parameters);
        }
        catch (InvalidOperationException error) when (error.Message == "订单状态不允许支付")
        {
            return Conflict(new { error = error.Message });
        }
        catch (InvalidOperationException)
        {
            // 不能撤销可能已送达微信的支付预约，也不暴露 HTTP 上游正文、密钥路径或签名。
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "微信支付暂时不可用，请核对支付状态后重试" });
        }
    }
}
