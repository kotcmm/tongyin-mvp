using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tongyin.Api.Auth;

namespace Tongyin.Api.Tests.Auth;

/// <summary>旧业务接口测试只关注受保护接口的业务行为，使用专门的测试身份代理。</summary>
public static class TestAdminAuthExtensions
{
    /// <summary>只为测试宿主配置模拟身份认证和模拟 CSRF；绝不用于生产。</summary>
    public static void AddTestAdminAuthentication(this IServiceCollection services, string? token)
    {
        services.AddSingleton(new AdminTestIdentity(token));
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAdminAuthHandler>(
            TestAdminAuthHandler.SchemeName, _ => { });
        services.PostConfigure<AuthorizationOptions>(options =>
        {
            options.AddPolicy(AuthorizationPolicies.Admin,
                new AuthorizationPolicyBuilder(TestAdminAuthHandler.SchemeName).RequireAuthenticatedUser().Build());
        });
        services.RemoveAll<IAntiforgery>();
        services.AddSingleton<IAntiforgery, FakeAdminAntiforgery>();
    }
}

/// <summary>固定测试凭据，仅在测试容器使用。</summary>
public sealed record AdminTestIdentity(string? ExpectedToken);

/// <summary>只接受来自自动测试的模拟 Bearer 请求，不能进入生产依赖注册。</summary>
public sealed class TestAdminAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AdminTestIdentity credential) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>与正式 Identity Cookie 完全隔离的单元测试认证方案。</summary>
    public const string SchemeName = "TestAdmin";

    /// <summary>模拟登录的管理员名称由凭据是否匹配决定。</summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (credential.ExpectedToken is null || header != "Bearer " + credential.ExpectedToken)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "admin"), new Claim(ClaimTypes.Name, "admin")], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>旧业务逻辑测试不涉及浏览器 CSRF，避免重写其业务验证；真实 CSRF 另测。</summary>
public sealed class FakeAdminAntiforgery : IAntiforgery
{
    /// <summary>在测试中模拟令牌签发。</summary>
    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context)
        => new("fake", "fake", "fake", "X-CSRF-TOKEN");

    /// <summary>在测试中模拟令牌读取。</summary>
    public AntiforgeryTokenSet GetTokens(HttpContext context)
        => new("fake", "fake", "fake", "X-CSRF-TOKEN");

    /// <summary>模拟允许后台业务请求通过 CSRF 检查。</summary>
    public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(true);

    /// <summary>模拟 CSRF 核验不抛出。</summary>
    public Task ValidateRequestAsync(HttpContext context) => Task.CompletedTask;

    /// <summary>模拟写出 CSRF Cookie。</summary>
    public void SetCookieTokenAndHeader(HttpContext context) { }
}
