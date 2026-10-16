using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Tongyin.Api.Auth;

/// <summary>
/// 将旧 Go 后台的 X-Admin-Token 认证接入 ASP.NET Core 管线，不混用微信登录身份。
/// </summary>
public sealed class AdminTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>管理员令牌专用认证方案名称。</summary>
    public const string SchemeName = "AdminToken";

    private readonly AdminTokenValidator _validator;

    /// <summary>注入管理员令牌验证能力和框架认证依赖。</summary>
    public AdminTokenAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AdminTokenValidator validator)
        : base(options, logger, encoder)
    {
        _validator = validator;
    }

    /// <summary>
    /// 从专用请求头验证管理员令牌，成功后生成独立的管理员身份。
    /// </summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Admin-Token", out var token) || token.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!_validator.IsValid(token.ToString()))
        {
            return Task.FromResult(AuthenticateResult.Fail("管理员令牌无效"));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "admin-token")],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
