using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tongyin.Api.Auth;
using Tongyin.Api.ViewModels.Admin;

namespace Tongyin.Api.Controllers.Admin;

/// <summary>管理员账号使用 ASP.NET Core Identity 与 HttpOnly Cookie，不自行签发认证令牌。</summary>
[ApiController]
[Route("api/admin/account")]
public sealed class AdminAccountController(
    UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signIn,
    AdminIdentityBootstrapper bootstrap,
    IAntiforgery antiforgery) : ControllerBase
{
    /// <summary>获取同源防 CSRF 请求令牌，供登录及后台全部写操作使用。</summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        var token = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { token = token.RequestToken });
    }

    /// <summary>初次使用部署机密建立单管理员，之后使用 Identity 密码检查及账号锁定。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("admin-login")]
    public async Task<IActionResult> LoginAsync([FromBody] AdminLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Unauthorized(new { error = "用户名或密码不正确" });
        }

        var user = await users.FindByNameAsync(request.Username);
        if (user is null)
        {
            await bootstrap.TryCreateAsync(request.Username, request.Password);
            user = await users.FindByNameAsync(request.Username);
        }

        var result = user is null ? SignInResult.Failed
            : await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return Unauthorized(new { error = "用户名或密码错误，或账号暂时被锁定" });
        }

        Response.Headers.CacheControl = "no-store";
        return Ok(new { username = user!.UserName, expires_in = 28800 });
    }

    /// <summary>读取 Identity Cookie 中的当前登录管理员名称。</summary>
    [HttpGet]
    [AdminAuthorize]
    public IActionResult Get() => Ok(new { username = User.Identity?.Name });

    /// <summary>使用 Identity 标准 API 校验密码及修改账户，完成后轮换安全戳与旧 Cookie。</summary>
    [HttpPut]
    [AdminAuthorize]
    public async Task<IActionResult> UpdateAsync([FromBody] AdminCredentialsUpdateRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized(new { error = "管理员会话失效" });
        if (!await users.CheckPasswordAsync(user, request.CurrentPassword))
        {
            return BadRequest(new { error = "当前密码不正确" });
        }

        if (request.NewUsername is null && request.NewPassword is null)
        {
            return BadRequest(new { error = "请修改用户名或密码" });
        }

        if (request.NewPassword is not null)
        {
            var changed = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!changed.Succeeded)
            {
                return BadRequest(new { error = string.Join("；", changed.Errors.Select(x => x.Description)) });
            }
        }

        if (request.NewUsername is not null && request.NewUsername != user.UserName)
        {
            var renamed = await users.SetUserNameAsync(user, request.NewUsername);
            if (!renamed.Succeeded)
            {
                return BadRequest(new { error = string.Join("；", renamed.Errors.Select(x => x.Description)) });
            }
        }

        var invalidated = await users.UpdateSecurityStampAsync(user);
        if (!invalidated.Succeeded) return Conflict(new { error = "账号已由其他请求修改，请重新登录" });
        await signIn.SignOutAsync();
        return Ok(new { username = user.UserName, relogin_required = true });
    }

    /// <summary>退出登录并轮换 Identity 安全戳，废止该账号的其他旧会话。</summary>
    [HttpPost("logout")]
    [AdminAuthorize]
    public async Task<IActionResult> LogoutAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is not null) await users.UpdateSecurityStampAsync(user);
        await signIn.SignOutAsync();
        return NoContent();
    }
}
