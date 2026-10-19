using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tongyin.Application.Payments;
using Tongyin.Infrastructure.Payments;

namespace Tongyin.Api.Controllers.Internal;

/// <summary>微信支付异步通知入口，不接受微信会话/管理员令牌作为资金凭据。</summary>
[ApiController]
[AllowAnonymous]
[Route("api/pay/notify")]
public sealed class WechatPayNotifyController(ConfirmWechatPayment confirmation, WechatPaymentAppIdResolver appIdResolver, WechatPayConfiguration settings) : ControllerBase
{
    private const int MaxBodyBytes = 1024 * 1024;

    /// <summary>原始正文验签、AES-GCM 解密、核对订单金额和 OpenID，幂等写入已付款状态。</summary>
    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (!settings.IsReady)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "FAIL", message = "支付回调尚未配置" });
        }

        if (Request.ContentLength > MaxBodyBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { code = "FAIL", message = "回调体积超限" });
        }

        if (!TryHeader("Wechatpay-Timestamp", out var timestamp)
            || !TryHeader("Wechatpay-Nonce", out var nonce)
            || !TryHeader("Wechatpay-Serial", out var serial)
            || !TryHeader("Wechatpay-Signature", out var signature))
        {
            return Unauthorized(new { code = "FAIL", message = "支付通知签名缺失" });
        }

        using var memory = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await Request.Body.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (memory.Length + count > MaxBodyBytes)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new { code = "FAIL", message = "回调体积超限" });
            }
            await memory.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        string body;
        try
        {
            body = new UTF8Encoding(false, true).GetString(memory.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return BadRequest(new { code = "FAIL", message = "回调内容编码错误" });
        }

        try
        {
            var appId = await appIdResolver.GetAsync(cancellationToken);
            await confirmation.ExecuteAsync(body, timestamp, nonce, signature, serial, appId, cancellationToken);
            return Ok(new { code = "SUCCESS", message = "成功" });
        }
        catch (PaymentNotificationValidationException)
        {
            return Unauthorized(new { code = "FAIL", message = "支付通知验签失败" });
        }
        catch (InvalidOperationException)
        {
            // 验签失败、商户和订单信息不符都不得回 200，微信可安全重发以待核查。
            return Conflict(new { code = "FAIL", message = "支付通知验签或订单校验失败" });
        }
    }

    private bool TryHeader(string name, out string value)
    {
        value = "";
        if (!Request.Headers.TryGetValue(name, out var header) || header.Count != 1 || string.IsNullOrWhiteSpace(header[0]))
        {
            return false;
        }
        value = header[0]!;
        return true;
    }
}
