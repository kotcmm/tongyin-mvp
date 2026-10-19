using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Api.Tests.Checkout;

/// <summary>从运营查看订单、开始制作到真实运单发货及客户查询的 HTTP 业务验收。</summary>
public sealed class FulfillmentEndpointTests
{
    /// <summary>运营凭据可以查看待制作订单，但不能向后台列表泄露微信 OpenID。</summary>
    [Fact]
    public async Task AdminOrders_ListsPaidOrdersWithoutOwner()
    {
        using var app = new FulfillmentFactory();
        app.SeedOrder("paid");
        using var client = app.CreateClient();
        using var response = await client.SendAsync(Admin(HttpMethod.Get, "/api/admin/orders"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var order = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("order-1", order.GetProperty("id").GetString());
        Assert.False(order.TryGetProperty("owner", out _));
    }

    /// <summary>运营人员确认收到款项后可开始制作，订单变更写入业务历史。</summary>
    [Fact]
    public async Task AdminStatus_PaidOrder_StartsPrinting()
    {
        using var app = new FulfillmentFactory();
        app.SeedOrder("paid");
        using var client = app.CreateClient();
        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/admin/orders/order-1/status", new { status = "printing" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("printing", app.Status());
        Assert.Contains("\"status.printing\"", app.Payload());
    }

    /// <summary>没有有效的微信支付状态或存在退款请求时不得开始制作。</summary>
    [Theory]
    [InlineData("awaiting_payment", "")]
    [InlineData("paid", "requested")]
    public async Task AdminStatus_UnpaidOrRefundRequested_RejectsProduction(string status, string refund)
    {
        using var app = new FulfillmentFactory();
        app.SeedOrder(status, refund);
        using var client = app.CreateClient();
        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/admin/orders/order-1/status", new { status = "printing" }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(status, app.Status());
    }

    /// <summary>必须制作中且提供真实格式运单，发货后订单保留时间、快递公司和历史。</summary>
    [Fact]
    public async Task AdminShipment_PrintingOrder_SavesRealTracking()
    {
        using var app = new FulfillmentFactory();
        app.SeedOrder("printing");
        using var client = app.CreateClient();
        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/admin/orders/order-1/shipment",
            new { carrier = " 顺丰速运 ", carrier_code = "SHUNFENG", tracking = " SF12345678901 " }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(app.Payload());
        Assert.Equal("shipped", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("顺丰速运", json.RootElement.GetProperty("carrier").GetString());
        Assert.Equal("shunfeng", json.RootElement.GetProperty("carrier_code").GetString());
        Assert.Equal("SF12345678901", json.RootElement.GetProperty("tracking").GetString());
        Assert.True(DateTimeOffset.TryParse(json.RootElement.GetProperty("shipped_at").GetString(), out _));
        Assert.Equal("unavailable", json.RootElement.GetProperty("tracking_state").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("tracking_events").GetArrayLength());
    }

    /// <summary>已经发货不得重复覆盖快递单号，非制作状态不得跳过状态发货。</summary>
    [Theory]
    [InlineData("paid")]
    [InlineData("shipped")]
    public async Task AdminShipment_WrongState_RejectsAndPreservesOrder(string status)
    {
        using var app = new FulfillmentFactory();
        app.SeedOrder(status);
        var original = app.Payload();
        using var client = app.CreateClient();
        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/admin/orders/order-1/shipment",
            new { carrier = "顺丰速运", carrier_code = "shunfeng", tracking = "SF12345678901" }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(original, app.Payload());
    }

    /// <summary>快递字段格式非法时直接拒绝，不能把订单置为已发货。</summary>
    [Fact]
    public async Task AdminShipment_InvalidTracking_DoesNotShip()
    {
        using var app = new FulfillmentFactory();
        app.SeedOrder("printing");
        using var client = app.CreateClient();
        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/admin/orders/order-1/shipment",
            new { carrier = "顺丰速运", carrier_code = "shunfeng", tracking = "?" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("printing", app.Status());
    }

    /// <summary>管理员业务接口仅接受后台认证，不接受客户 Bearer，缺失管理员凭据返回 401。</summary>
    [Fact]
    public async Task AdminShipment_WithoutAdminToken_ReturnsUnauthorized()
    {
        using var app = new FulfillmentFactory();
        app.SeedOrder("printing");
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/admin/orders/order-1/shipment",
            new { carrier = "顺丰速运", carrier_code = "shunfeng", tracking = "SF12345678901" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("printing", app.Status());
    }

    /// <summary>客户只可查询本人已发货订单，未知物流轨迹必须返回空数组而非虚构记录。</summary>
    [Fact]
    public async Task CustomerTracking_ShippedOrder_ReturnsOwnTrackingOnly()
    {
        using var app = new FulfillmentFactory();
        app.SeedOrder("shipped");
        app.SeedUser("owner-1");
        app.SeedUser("owner-2");
        using var client = app.CreateClient();
        using var own = await client.SendAsync(Customer("owner-1", "/api/orders/order-1/tracking"));
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var json = JsonDocument.Parse(await own.Content.ReadAsStringAsync());
        Assert.Equal("SF12345678901", json.RootElement.GetProperty("tracking_no").GetString());
        Assert.Equal("unavailable", json.RootElement.GetProperty("source").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("events").GetArrayLength());
        using var foreign = await client.SendAsync(Customer("owner-2", "/api/orders/order-1/tracking"));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    private static HttpRequestMessage Admin(HttpMethod method, string path, object? payload = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-admin-token");
        if (payload is not null) request.Content = JsonContent.Create(payload);
        return request;
    }

    private static HttpRequestMessage Customer(string owner, string path)
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{owner}|{expiry}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(FulfillmentFactory.Secret), Encoding.UTF8.GetBytes(payload));
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", payload + "." +
            Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        return request;
    }

    private sealed class FulfillmentFactory : WebApplicationFactory<Program>
    {
        public const string Secret = "test-fulfillment-session-secret-long-enough";
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "tongyin-fulfillment-" + Guid.NewGuid().ToString("N"));
        private string DbPath => Path.Combine(_directory, "fulfillment.db");

        public FulfillmentFactory()
        {
            Directory.CreateDirectory(_directory);
            using var db = Connect();
            db.Open();
            db.Execute("""
                CREATE TABLE ty_users(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_orders(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_audit(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_store_lock(id INTEGER PRIMARY KEY);
                INSERT INTO ty_store_lock(id) VALUES(1);
                """);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = Secret, ["DB_CONNECTION_STRING"] = "in-memory-tests",
                ["DB_DRIVER"] = "postgres", ["ADMIN_TOKEN"] = "test-admin-token",
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60", ["AI_MODEL_DISPATCH_POLL_SECONDS"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                Tongyin.Api.Tests.Auth.TestAdminAuthExtensions.AddTestAdminAuthentication(services, "test-admin-token");
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
                services.RemoveAll<IQueuedImageGenerationReader>();
                services.AddSingleton<IQueuedImageGenerationReader, EmptyReader>();
            });
        }

        public void SeedOrder(string status, string refundStatus = "")
        {
            using var db = Connect();
            db.Open();
            var payload = JsonSerializer.Serialize(new
            {
                id = "order-1", owner = "owner-1", work_id = "work-1",
                created_at = "2026-10-17T09:00:00+08:00", name = "张三", phone = "13800138000",
                address = "广州市海珠区新港东路100号", status, refund_status = refundStatus,
                price_fen = 12900, carrier = status == "shipped" ? "顺丰速运" : "",
                carrier_code = status == "shipped" ? "shunfeng" : "",
                tracking = status == "shipped" ? "SF12345678901" : "",
                history = new[] { new { at = "2026-10-17T09:00:00+08:00", actor = "wechatpay", action = "paid" } }
            });
            db.Execute("INSERT INTO ty_orders(id,owner_id,created_at,payload) VALUES ('order-1','owner-1','2026-10-17',@Payload)",
                new { Payload = payload });
        }

        public void SeedUser(string owner)
        {
            using var db = Connect();
            db.Open();
            db.Execute("INSERT INTO ty_users(id,owner_id,created_at,payload) VALUES (@Owner,@Owner,'2026-10-17',@Payload)",
                new { Owner = owner, Payload = JsonSerializer.Serialize(new { owner, disabled = false }) });
        }

        public string Payload()
        {
            using var db = Connect();
            db.Open();
            return db.QuerySingle<string>("SELECT payload FROM ty_orders WHERE id='order-1'");
        }

        public string Status() => JsonDocument.Parse(Payload()).RootElement.GetProperty("status").GetString()!;

        private DbConnection Connect() => new SqliteConnection($"Data Source={DbPath};Pooling=False");

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private sealed class EmptyReader : IQueuedImageGenerationReader
        {
            public Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<QueuedImageGeneration>>([]);
        }
    }
}
