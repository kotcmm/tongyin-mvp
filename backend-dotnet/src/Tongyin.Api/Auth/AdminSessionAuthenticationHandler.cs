using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Tongyin.Application.Admin;

namespace Tongyin.Api.Auth;

/// <summary>以数据库校验的管理员短期 Bearer 会话认证后台操作，不接受旧 X-Admin-Token。</summary>
public sealed class AdminSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IAdminSessionVerifier accounts) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>管理员专用认证方案名称，与微信会话和 AI 回调严格隔离。</summary>
    public const string SchemeName = "AdminSession";

    /// <summary>验证签名会话和数据库凭据版本后创建管理员身份。</summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var name = await accounts.VerifySessionAsync(header["Bearer ".Length..], Context.RequestAborted);
        if (name is null)
        {
            return AuthenticateResult.Fail("管理员会话已失效");
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, name), new Claim(ClaimTypes.Name, name)],
            Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
