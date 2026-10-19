using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tongyin.Api.Auth;
using Tongyin.Api.ViewModels.Admin;
using Tongyin.Application.Admin;

namespace Tongyin.Api.Controllers.Admin;

/// <summary>独立后台登录、注销及管理员自助维护账号接口。</summary>
[ApiController]
[Route("api/admin/account")]
public sealed class AdminAccountController(AdminAccountService accounts) : ControllerBase
{
    /// <summary>使用用户名及密码登录；首次登录时仅允许使用服务器配置的初始化账号。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("admin-login")]
    public async Task<IActionResult> LoginAsync([FromBody] AdminLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.LoginAsync(request.Username, request.Password, cancellationToken);
        if (result is null)
        {
            return Unauthorized(new { error = "用户名或密码不正确，或后台尚未初始化" });
        }

        Response.Headers.CacheControl = "no-store";
        return Ok(new { token = result.Value.Token, username = result.Value.Username, expires_in = 28800 });
    }

    /// <summary>查看当前已登录管理员的用户名，不返回密码摘要或初始化凭据。</summary>
    [HttpGet]
    [AdminAuthorize]
    public IActionResult Get() => Ok(new { username = User.Identity?.Name });

    /// <summary>核对当前密码后修改用户名和/或密码；修改成功将废止所有旧会话。</summary>
    [HttpPut]
    [AdminAuthorize]
    public async Task<IActionResult> UpdateAsync([FromBody] AdminCredentialsUpdateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var username = await accounts.UpdateAsync(request.CurrentPassword, request.NewUsername, request.NewPassword, cancellationToken);
            return Ok(new { username, relogin_required = true });
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { error = "当前密码不正确" });
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { error = error.Message });
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { error = "管理员账户已变化，请重新登录" });
        }
    }

    /// <summary>管理员主动退出会话，并撤销此前该账户全部有效会话。</summary>
    [HttpPost("logout")]
    [AdminAuthorize]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        await accounts.LogoutAsync(cancellationToken);
        return NoContent();
    }
}
