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

/// <summary>小程序从模型确认到收货信息与待付款订单的端到端接口契约。</summary>
public sealed class CheckoutEndpointTests
{
    private const string ValidName = "张三";
    private const string ValidPhone = "13800138000";
    private const string ValidAddress = "广州市海珠区新港东路100号";
    private static object Shipping => new { name = ValidName, phone = ValidPhone, address = ValidAddress };
    private static object OrderBody => new { work_id = "work-1", name = ValidName, phone = ValidPhone, address = ValidAddress };

    /// <summary>首次读取本人地址时，应与 Go 一致返回 address:null 而不是 404。</summary>
    [Fact]
    public async Task Shipping_FirstVisit_ReturnsNull()
    {
        using var app = new CheckoutApiFactory();
        app.SeedUser("owner-1");
        using var client = app.CreateClient();
        using var request = Auth("owner-1", HttpMethod.Get, "/api/shipping-address");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("address").ValueKind);
    }

    /// <summary>保存地址再读出时只返回当前用户的信息，禁止向客户端返回 OpenID。</summary>
    [Fact]
    public async Task Shipping_PutThenGet_RoundTripsWithoutOwnerField()
    {
        using var app = new CheckoutApiFactory();
        app.SeedUser("owner-1");
        using var client = app.CreateClient();
        using var put = Auth("owner-1", HttpMethod.Put, "/api/shipping-address", Shipping);

        using var saved = await client.SendAsync(put);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var get = Auth("owner-1", HttpMethod.Get, "/api/shipping-address");
        using var response = await client.SendAsync(get);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var address = document.RootElement.GetProperty("address");
        Assert.Equal(ValidName, address.GetProperty("name").GetString());
        Assert.False(address.TryGetProperty("owner", out _));
        Assert.False(address.TryGetProperty("owner_id", out _));
    }

    /// <summary>收货信息必须通过服务端校验，非法内容不会写入数据库。</summary>
    [Fact]
    public async Task Shipping_InvalidBody_ReturnsBadRequest()
    {
        using var app = new CheckoutApiFactory();
        app.SeedUser("owner-1");
        using var client = app.CreateClient();
        using var put = Auth("owner-1", HttpMethod.Put, "/api/shipping-address",
            new { name = "张", phone = "abc", address = "短" });

        using var response = await client.SendAsync(put);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.Count("ty_addresses"));
    }

    /// <summary>已就绪模型创建订单进入 awaiting_payment，并支持列表、详情和作品关联查询。</summary>
    [Fact]
    public async Task Order_CreateThenRead_AllCustomerRoutesReturnUnpaidOrder()
    {
        using var app = new CheckoutApiFactory();
        app.SeedUser("owner-1");
        app.SeedWork("work-1", "owner-1", "model_ready");
        using var client = app.CreateClient();
        using var create = Auth("owner-1", HttpMethod.Post, "/api/orders", OrderBody);

        using var response = await client.SendAsync(create);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var order = created.RootElement;
        var id = order.GetProperty("id").GetString()!;
        Assert.Equal(24, id.Length);
        Assert.Equal("awaiting_payment", order.GetProperty("status").GetString());
        Assert.Equal(12900, order.GetProperty("price_fen").GetInt32());
        Assert.Equal("work-1", order.GetProperty("work_id").GetString());
        Assert.False(order.TryGetProperty("owner", out _));

        using var detailRequest = Auth("owner-1", HttpMethod.Get, "/api/orders/" + id);
        using var detail = await client.SendAsync(detailRequest);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        using var listRequest = Auth("owner-1", HttpMethod.Get, "/api/orders");
        using var list = await client.SendAsync(listRequest);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(id, Assert.Single(listJson.RootElement.EnumerateArray()).GetProperty("id").GetString());

        using var workOrderRequest = Auth("owner-1", HttpMethod.Get, "/api/works/work-1/order");
        using var workOrder = await client.SendAsync(workOrderRequest);
        using var workJson = JsonDocument.Parse(await workOrder.Content.ReadAsStringAsync());
        Assert.Equal(id, workJson.RootElement.GetProperty("order").GetProperty("id").GetString());
        Assert.Equal(1, app.Count("ty_orders"));
    }

    /// <summary>同作品不允许创建两个未取消的订单，重复请求必须返回冲突。</summary>
    [Fact]
    public async Task Order_DuplicateCreation_ReturnsConflictWithoutSecondOrder()
    {
        using var app = new CheckoutApiFactory();
        app.SeedUser("owner-1");
        app.SeedWork("work-1", "owner-1", "model_ready");
        using var client = app.CreateClient();
        using var first = Auth("owner-1", HttpMethod.Post, "/api/orders", OrderBody);
        using var second = Auth("owner-1", HttpMethod.Post, "/api/orders", OrderBody);

        using var a = await client.SendAsync(first);
        using var b = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, b.StatusCode);
        Assert.Equal(1, app.Count("ty_orders"));
    }

    /// <summary>模型未完成或其他用户作品均不能提交订单。</summary>
    [Theory]
    [InlineData("image_ready", "owner-1")]
    [InlineData("model_pending", "owner-1")]
    [InlineData("model_ready", "owner-2")]
    public async Task Order_NotReadyOrForeign_ReturnsConflict(string status, string workOwner)
    {
        using var app = new CheckoutApiFactory();
        app.SeedUser("owner-1");
        app.SeedWork("work-1", workOwner, status);
        using var client = app.CreateClient();
        using var request = Auth("owner-1", HttpMethod.Post, "/api/orders", OrderBody);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, app.Count("ty_orders"));
    }

    /// <summary>没有认证或他人账号无法读取订单和地址。</summary>
    [Fact]
    public async Task Order_AccessControl_OnlyOwnerCanRead()
    {
        using var app = new CheckoutApiFactory();
        app.SeedUser("owner-1");
        app.SeedUser("owner-2");
        app.SeedWork("work-1", "owner-1", "model_ready");
        using var client = app.CreateClient();
        using var create = Auth("owner-1", HttpMethod.Post, "/api/orders", OrderBody);
        using var created = await client.SendAsync(create);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetString();

        using var forbiddenRequest = Auth("owner-2", HttpMethod.Get, "/api/orders/" + id);
        using var forbidden = await client.SendAsync(forbiddenRequest);
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        using var foreignListRequest = Auth("owner-2", HttpMethod.Get, "/api/orders");
        using var foreignList = await client.SendAsync(foreignListRequest);
        using var json = JsonDocument.Parse(await foreignList.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.EnumerateArray());
        using var anonymous = await client.GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    /// <summary>只允许在待付款状态修改订单收货快照，不应覆盖历史价款。</summary>
    [Fact]
    public async Task Order_UpdateShipping_PreservesPrice()
    {
        using var app = new CheckoutApiFactory();
        app.SeedUser("owner-1");
        app.SeedWork("work-1", "owner-1", "model_ready");
        using var client = app.CreateClient();
        using var create = Auth("owner-1", HttpMethod.Post, "/api/orders", OrderBody);
        using var created = await client.SendAsync(create);
        using var original = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = original.RootElement.GetProperty("id").GetString();

        using var change = Auth("owner-1", HttpMethod.Put, "/api/orders/" + id + "/shipping-address",
            new { name = "李四", phone = ValidPhone, address = ValidAddress });
        using var modified = await client.SendAsync(change);

        Assert.Equal(HttpStatusCode.OK, modified.StatusCode);
        using var updated = JsonDocument.Parse(await modified.Content.ReadAsStringAsync());
        Assert.Equal("李四", updated.RootElement.GetProperty("name").GetString());
        Assert.Equal(12900, updated.RootElement.GetProperty("price_fen").GetInt32());
    }

    /// <summary>只有没有发起支付的待付款订单可以取消，取消后允许同作品重新下单。</summary>
    [Fact]
    public async Task Order_CancelThenCreateAgain_IsAllowed()
    {
        using var app = new CheckoutApiFactory();
        app.SeedUser("owner-1");
        app.SeedWork("work-1", "owner-1", "model_ready");
        using var client = app.CreateClient();
        using var create = Auth("owner-1", HttpMethod.Post, "/api/orders", OrderBody);
        using var created = await client.SendAsync(create);
        using var original = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = original.RootElement.GetProperty("id").GetString();

        using var cancel = Auth("owner-1", HttpMethod.Post, "/api/orders/" + id + "/cancel", new { reason = "修改订单" });
        using var canceled = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        using var canceledJson = JsonDocument.Parse(await canceled.Content.ReadAsStringAsync());
        Assert.Equal("canceled", canceledJson.RootElement.GetProperty("status").GetString());

        using var retry = Auth("owner-1", HttpMethod.Post, "/api/orders", OrderBody);
        using var createdAgain = await client.SendAsync(retry);
        Assert.Equal(HttpStatusCode.Created, createdAgain.StatusCode);
        Assert.Equal(2, app.Count("ty_orders"));
    }

    private static HttpRequestMessage Auth(string owner, HttpMethod method, string path, object? body = null)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{owner}|{expires}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(CheckoutApiFactory.Secret), Encoding.UTF8.GetBytes(payload));
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", payload + "." +
            Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed class CheckoutApiFactory : WebApplicationFactory<Program>
    {
        public const string Secret = "checkout-test-session-secret-with-enough-entropy";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-checkout-" + Guid.NewGuid().ToString("N"));
        private string DbPath => Path.Combine(_root, "checkout.db");

        public CheckoutApiFactory()
        {
            Directory.CreateDirectory(_root);
            using var db = Connect();
            db.Open();
            db.Execute("""
                CREATE TABLE ty_users(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_works(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_orders(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_addresses(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_store_lock(id INTEGER PRIMARY KEY);
                INSERT INTO ty_store_lock(id) VALUES (1);
                """);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = Secret, ["DB_DRIVER"] = "postgres", ["DB_CONNECTION_STRING"] = "unused-for-tests",
                ["PRODUCT_PRICE_FEN"] = "12900", ["DATA_DIR"] = _root,
                ["PUBLIC_URL"] = "http://localhost", ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60",
                ["AI_MODEL_DISPATCH_POLL_SECONDS"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
                services.RemoveAll<IQueuedImageGenerationReader>();
                services.AddSingleton<IQueuedImageGenerationReader, EmptyReader>();
            });
        }

        public void SeedUser(string owner)
        {
            using var db = Connect();
            db.Open();
            db.Execute("INSERT INTO ty_users(id,owner_id,created_at,payload) VALUES (@Id,@Owner,'2026-10-17',@Payload)",
                new { Id = owner, Owner = owner, Payload = JsonSerializer.Serialize(new { owner, disabled = false }) });
        }

        public void SeedWork(string id, string owner, string status)
        {
            using var db = Connect();
            db.Open();
            var payload = JsonSerializer.Serialize(new { id, owner, status, created_at = "2026-10-17T09:00:00+08:00",
                original = "original.png", model = status == "model_ready" ? "model.glb" : "",
                selected = 0, candidates = new[] { "image.png" }, image_attempts = 1,
                generation_id = "job-1" });
            db.Execute("INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES (@Id,@Owner,'2026-10-17',@Payload)",
                new { Id = id, Owner = owner, Payload = payload });
        }

        public int Count(string table)
        {
            using var db = Connect();
            db.Open();
            return db.ExecuteScalar<int>("SELECT COUNT(*) FROM " + (table == "ty_orders" ? "ty_orders" : "ty_addresses"));
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DbPath};Pooling=False");

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private sealed class EmptyReader : IQueuedImageGenerationReader
        {
            public Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<QueuedImageGeneration>>([]);
        }
    }
}
