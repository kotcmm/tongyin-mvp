using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tongyin.Application.Checkout;
using Tongyin.Domain.Checkout;
using Xunit;

namespace Tongyin.Api.Tests.Checkout;

/// <summary>运营人员从 .NET 单体后台页面进入订单制作与发货的部署契约。</summary>
public sealed class AdminWebEndpointTests
{
    /// <summary>访问 /admin 会跳到同源后台，避免相对静态资源路径不一致。</summary>
    [Fact]
    public async Task AdminRoot_RedirectsToConsole()
    {
        using var app = new AdminWebFactory();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var result = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/admin/", result.Headers.Location?.ToString());
    }

    /// <summary>后台页面由 .NET 服务提供，登录使用 ADMIN_TOKEN 而非尚未迁移的管理员账号接口。</summary>
    [Fact]
    public async Task AdminConsole_ServesTokenLoginAndOrdersAssets()
    {
        using var app = new AdminWebFactory();
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/admin/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("id=\"adminToken\"", html);
        Assert.Contains("/admin/orders.js", html);
        Assert.Contains("/admin/app.css", html);
        Assert.DoesNotContain("/api/admin/auth/login", html);
        Assert.DoesNotContain("test-admin-secret", html);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    /// <summary>运营静态资源必须真实可读取，且页面脚本使用受保护的订单接口。</summary>
    [Fact]
    public async Task AdminConsole_ServesStylesAndOrderWorkflow()
    {
        using var app = new AdminWebFactory();
        using var client = app.CreateClient();
        using var css = await client.GetAsync("/admin/app.css");
        using var js = await client.GetAsync("/admin/orders.js");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        var styles = await css.Content.ReadAsStringAsync();
        var script = await js.Content.ReadAsStringAsync();
        Assert.Contains(".nav-item", styles);
        Assert.Contains("/api/admin/orders", script);
        Assert.Contains("/status", script);
        Assert.Contains("/shipment", script);
        Assert.Contains("X-Admin-Token", script);
        Assert.DoesNotContain("/api/admin/auth/login", script);
        Assert.DoesNotContain("test-admin-secret", script);
    }

    /// <summary>静态页可供登录，但未认证不能读取包含个人收货信息的后台订单。</summary>
    [Fact]
    public async Task AdminOrders_WithoutToken_RejectsPersonalData()
    {
        using var app = new AdminWebFactory();
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/api/admin/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, app.Orders.Reads);
    }

    /// <summary>管理员有效令牌登录后页面调用的订单接口可得到待制作与待发货数据。</summary>
    [Fact]
    public async Task AdminOrders_WithToken_ReturnsFulfillmentQueue()
    {
        using var app = new AdminWebFactory();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/orders");
        request.Headers.Add("X-Admin-Token", "test-admin-secret");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"paid\"", json);
        Assert.Contains("\"status\":\"printing\"", json);
        Assert.Equal(1, app.Orders.Reads);
    }

    private sealed class AdminWebFactory : WebApplicationFactory<Program>
    {
        public FakeOrders Orders { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "admin-web-test-secret-adequate-length",
                ["DB_DRIVER"] = "postgres",
                ["DB_CONNECTION_STRING"] = "unused",
                ["ADMIN_TOKEN"] = "test-admin-secret",
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60",
                ["AI_MODEL_DISPATCH_POLL_SECONDS"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrderFulfillmentRepository>();
                services.AddSingleton<IOrderFulfillmentRepository>(Orders);
            });
        }
    }

    private sealed class FakeOrders : IOrderFulfillmentRepository
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<OrderSnapshot>> ListAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<OrderSnapshot>>([
                new OrderSnapshot { Id = "order-paid", Status = "paid" },
                new OrderSnapshot { Id = "order-printing", Status = "printing" }
            ]);
        }

        public Task<OrderSnapshot?> StartPrintingAsync(string id, string actor, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderSnapshot?> ShipAsync(string id, string actor, ShipmentDetails details, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderTrackingSnapshot?> GetTrackingAsync(string owner, string id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
