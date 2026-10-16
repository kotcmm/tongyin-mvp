using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tongyin.Api.Auth;
using Tongyin.Api.Controllers;
using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Api.Tests.Ai;

/// <summary>
/// 从真实 ASP.NET Core 认证授权管线验证管理员令牌隔离，避免 Controller 直接检查请求头。
/// </summary>
public sealed class AdminAiTasksControllerTests
{
    private const string Url = "/api/admin/ai/stalled-image-tasks";

    /// <summary>
    /// 没有管理员令牌时认证中间件返回 401，执行方法和数据库均不能被调用。
    /// </summary>
    [Fact]
    public async Task GetStalled_WithoutToken_RejectsBeforeQuery()
    {
        using var app = new AdminAiApiFactory("test-admin-secret");
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, app.Reader.Calls);
    }

    /// <summary>
    /// 错误管理员令牌在认证层返回 401，不允许查询跨用户任务。
    /// </summary>
    [Fact]
    public async Task GetStalled_WrongToken_RejectsBeforeQuery()
    {
        using var app = new AdminAiApiFactory("test-admin-secret");
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.Add("X-Admin-Token", "wrong-secret");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, app.Reader.Calls);
    }

    /// <summary>
    /// 未配置 ADMIN_TOKEN 时，即使提供请求头也必须拒绝。
    /// </summary>
    [Fact]
    public async Task GetStalled_UnconfiguredAdminToken_Rejects()
    {
        using var app = new AdminAiApiFactory(null);
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.Add("X-Admin-Token", "anything");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, app.Reader.Calls);
    }

    /// <summary>
    /// 通过管理员认证后进入纯业务 Controller，返回固定 15 分钟阈值的只读任务清单。
    /// </summary>
    [Fact]
    public async Task GetStalled_ValidAdminToken_ReturnsTasks()
    {
        using var app = new AdminAiApiFactory("test-admin-secret");
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.Add("X-Admin-Token", "test-admin-secret");
        var before = DateTimeOffset.UtcNow;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var task = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("work-001", task.GetProperty("work_id").GetString());
        Assert.Equal("job-001", task.GetProperty("generation_id").GetString());
        Assert.Equal(1, app.Reader.Calls);
        Assert.Equal(100, app.Reader.LastLimit);
        Assert.InRange(app.Reader.LastCutoff, before.AddMinutes(-16), before.AddMinutes(-14));
    }

    /// <summary>
    /// 管理员令牌不能作为微信用户凭据读取普通用户作品。
    /// </summary>
    [Fact]
    public async Task GetWorks_AdminTokenOnly_IsNotWechatAuthorization()
    {
        using var app = new AdminAiApiFactory("test-admin-secret");
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/works");
        request.Headers.Add("X-Admin-Token", "test-admin-secret");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Admin 授权策略必须使用独立认证方案，不能套用微信用户启用校验。
    /// </summary>
    [Fact]
    public async Task AdminPolicy_UsesOnlyAdminTokenScheme()
    {
        using var app = new AdminAiApiFactory("test-admin-secret");
        using var client = app.CreateClient();
        var policies = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await policies.GetPolicyAsync(AuthorizationPolicies.Admin);

        Assert.NotNull(policy);
        Assert.Contains(AdminTokenAuthenticationHandler.SchemeName, policy.AuthenticationSchemes);
        Assert.DoesNotContain(WechatSessionAuthenticationHandler.SchemeName, policy.AuthenticationSchemes);
        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    /// <summary>
    /// 管理端 Controller 只声明授权标记，不依赖具体令牌校验器。
    /// </summary>
    [Fact]
    public void AdminController_UsesDedicatedAuthorizeAttribute()
    {
        var attribute = Assert.Single(typeof(AdminAiTasksController).GetCustomAttributes(typeof(AdminAuthorizeAttribute), inherit: true));

        Assert.Equal(AuthorizationPolicies.Admin, Assert.IsType<AdminAuthorizeAttribute>(attribute).Policy);
    }

    private sealed class AdminAiApiFactory(string? adminToken) : WebApplicationFactory<Program>
    {
        public RecordingReader Reader { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "test-session-key-with-adequate-entropy",
                ["DB_CONNECTION_STRING"] = "unused-in-test",
                ["DB_DRIVER"] = "postgres",
                ["ADMIN_TOKEN"] = adminToken
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStalledImageTasksReader>();
                services.AddSingleton<IStalledImageTasksReader>(Reader);
            });
        }
    }

    private sealed class RecordingReader : IStalledImageTasksReader
    {
        public int Calls { get; private set; }
        public DateTimeOffset LastCutoff { get; private set; }
        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<StalledImageTask>> ListAsync(DateTimeOffset startedBefore, int limit, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastCutoff = startedBefore;
            LastLimit = limit;
            return Task.FromResult<IReadOnlyList<StalledImageTask>>(
                [new StalledImageTask
                {
                    WorkId = "work-001", GenerationId = "job-001",
                    StartedAt = startedBefore.AddHours(-1), ImageAttempts = 1,
                    ProviderId = "seedream-01", IsLegacyGo = false
                }]);
        }
    }
}
