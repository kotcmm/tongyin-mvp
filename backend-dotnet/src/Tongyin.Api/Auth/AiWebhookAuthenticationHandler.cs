using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Tongyin.Api.Auth;

/// <summary>使用旧 Go 的 X-AI-Token 协议，对 AI worker 回调提供独立的框架认证方案。</summary>
public sealed class AiWebhookAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>AI 生成结果回调的专用认证方案名称。</summary>
    public const string SchemeName = "AiWebhook";

    private readonly IConfiguration _configuration;

    /// <summary>初始化 ASP.NET Core 原生认证处理器，不依赖管理员或微信令牌。</summary>
    public AiWebhookAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    /// <summary>验证唯一专用头与已配置令牌；比较哈希值避免泄露密钥内容或比较耗时。</summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = _configuration["AI_WEBHOOK_TOKEN"];
        if (string.IsNullOrWhiteSpace(expected)
            || !Request.Headers.TryGetValue("X-AI-Token", out var provided)
            || provided.Count != 1
            || string.IsNullOrWhiteSpace(provided.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var actualDigest = SHA256.HashData(Encoding.UTF8.GetBytes(provided.ToString()));
        var expectedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        if (!CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
        {
            return Task.FromResult(AuthenticateResult.Fail("AI 回调凭据无效"));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "ai-worker")], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
