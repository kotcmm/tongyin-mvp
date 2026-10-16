using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Tongyin.Api.ViewModels.Auth;
using Tongyin.Application.Auth;

namespace Tongyin.Api.Controllers;

/// <summary>
/// 小程序微信登录入口，采用服务端会话签发，不返回微信 session_key 或 OpenID。
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class WechatAuthController(LoginWechat loginWechat) : ControllerBase
{
    /// <summary>兑换一次性微信凭证、创建用户并返回服务端会话令牌。</summary>
    [HttpPost("wechat")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginAsync([FromBody] WechatLoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "微信登录凭证不能为空" });
        }

        try
        {
            var token = await loginWechat.ExecuteAsync(request.Code, cancellationToken);
            return Ok(new { token });
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "微信登录凭证无效" });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "账户已停用" });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "微信登录服务暂不可用" });
        }
        catch (JsonException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "微信登录服务暂不可用" });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "微信登录服务暂不可用" });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "微信登录服务暂不可用" });
        }
    }
}
