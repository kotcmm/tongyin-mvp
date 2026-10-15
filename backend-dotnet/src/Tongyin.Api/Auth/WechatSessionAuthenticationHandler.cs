using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Tongyin.Infrastructure.Auth;

namespace Tongyin.Api.Auth;

/// <summary>
/// 将 Go 服务签发的微信会话令牌接入 ASP.NET Core 身份认证管线。
/// </summary>
public sealed class WechatSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// 微信旧版会话令牌的身份认证方案名称。
    /// </summary>
    public const string SchemeName = "WechatSession";

    private readonly GoSessionTokenValidator _tokenValidator;

    /// <summary>
    /// 使用会话令牌验证器构造身份认证处理器。
    /// </summary>
    public WechatSessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, GoSessionTokenValidator tokenValidator)
        : base(options, logger, encoder)
    {
        _tokenValidator = tokenValidator;
    }

    /// <summary>
    /// 将有效的 Bearer 会话令牌转换为包含微信 OpenID 的已认证身份。
    /// </summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!_tokenValidator.TryValidate(authorization["Bearer ".Length..], DateTimeOffset.UtcNow, out var openId))
        {
            return Task.FromResult(AuthenticateResult.Fail("登录令牌无效或已过期"));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, openId)],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
