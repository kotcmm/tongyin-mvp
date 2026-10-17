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
using Tongyin.Application.Auth;
using Tongyin.Application.Checkout;
using Tongyin.Application.Payments;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Payments;
using Xunit;

namespace Tongyin.Api.Tests.Payments;

/// <summary>小程序预下单、专用支付通知验签与订单幂等回写的真实 ASP.NET Core 路由测试。</summary>
public sealed class WechatPaymentEndpointTests
{
    /// <summary>未登录者不能发起支付或修改支付预约。</summary>
    [Fact]
    public async Task Pay_AnonymousRequest_ReturnsUnauthorizedWithoutReservation()
    {
        using var app = new PaymentFactory();
        app.SeedOrder();
        using var client = app.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/orders/order-1/pay", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, app.Gateway.Calls);
        Assert.Equal(string.Empty, app.PaymentStartedAt());
    }

    /// <summary>支付参数返回小程序格式，且真实支付发生前订单仍为待付款。</summary>
    [Fact]
    public async Task Pay_ReadyOrder_ReservesAndReturnsMiniappPaymentParameters()
    {
        using var app = new PaymentFactory();
        app.SeedOrder();
        using var client = app.CreateClient();
        using var request = SignedRequest("owner-1", HttpMethod.Post, "/api/orders/order-1/pay");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RSA", json.RootElement.GetProperty("signType").GetString());
        Assert.Equal("prepay_id=test-prepay", json.RootElement.GetProperty("package").GetString());
        Assert.Equal("wx-test-app", app.Gateway.LastAppId);
        Assert.Equal("owner-1", app.Gateway.LastOpenId);
        Assert.Equal(12900, app.Gateway.LastPriceFen);
        Assert.NotEmpty(app.PaymentStartedAt());
        Assert.Equal("awaiting_payment", app.Status());
    }

    /// <summary>商户配置未完成时不发出请求，也不将订单标记为已开始支付。</summary>
    [Fact]
    public async Task Pay_NotConfigured_ReturnsUnavailable()
    {
        using var app = new PaymentFactory();
        app.Gateway.Configured = false;
        app.SeedOrder();
        using var client = app.CreateClient();
        using var request = SignedRequest("owner-1", HttpMethod.Post, "/api/orders/order-1/pay");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(app.PaymentStartedAt());
    }

    /// <summary>没有完整微信官方回调头不能修改订单。</summary>
    [Fact]
    public async Task Notify_Unsigned_ReturnsUnauthorized()
    {
        using var app = new PaymentFactory();
        app.SeedOrder();
        using var client = app.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/pay/notify", new { event_type = "TRANSACTION.SUCCESS" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("awaiting_payment", app.Status());
        Assert.Equal(0, app.Verifier.Calls);
    }

    /// <summary>来自经过验签的微信成功交易应修改状态；同一通知重放也只登记一次。</summary>
    [Fact]
    public async Task Notify_ValidThenRepeated_SettlesExactlyOnce()
    {
        using var app = new PaymentFactory();
        app.SeedOrder();
        using var client = app.CreateClient();

        using var first = NotifyRequest();
        using var settled = await client.SendAsync(first);
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
        Assert.Equal("paid", app.Status());

        using var replay = NotifyRequest();
        using var duplicate = await client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using var payload = JsonDocument.Parse(app.ReadOrder());
        Assert.Equal(2, payload.RootElement.GetProperty("history").GetArrayLength());
        Assert.Equal("tx-001", payload.RootElement.GetProperty("wechat_transaction_id").GetString());
    }

    /// <summary>即使签名通过，实际支付金额错误也不能宣告订单支付成功。</summary>
    [Fact]
    public async Task Notify_ValidSignatureWrongAmount_ReturnsConflict()
    {
        using var app = new PaymentFactory();
        app.SeedOrder();
        app.Verifier.Result = new VerifiedPaymentNotification
        {
            AppId = "wx-test-app", MchId = "mch-1", OutTradeNo = "order-1",
            TransactionId = "tx-001", PayerOpenId = "owner-1", TotalFen = 1
        };
        using var client = app.CreateClient();
        using var request = NotifyRequest();

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("awaiting_payment", app.Status());
    }

    /// <summary>回调验签器拒绝伪造签名时必须以 401 拒绝，不得假装订单成功入账。</summary>
    [Fact]
    public async Task Notify_FailedSignature_ReturnsUnauthorized()
    {
        using var app = new PaymentFactory();
        app.SeedOrder();
        app.Verifier.Reject = true;
        using var client = app.CreateClient();
        using var request = NotifyRequest();

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("awaiting_payment", app.Status());
    }

    private static HttpRequestMessage NotifyRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pay/notify")
        {
            Content = JsonContent.Create(new { event_type = "TRANSACTION.SUCCESS" })
        };
        request.Headers.Add("Wechatpay-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        request.Headers.Add("Wechatpay-Nonce", "nonce");
        request.Headers.Add("Wechatpay-Serial", "platform-serial");
        request.Headers.Add("Wechatpay-Signature", "signature");
        return request;
    }

    private static HttpRequestMessage SignedRequest(string owner, HttpMethod method, string path)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{owner}|{expires}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(PaymentFactory.Secret), Encoding.UTF8.GetBytes(payload));
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", payload + "." +
            Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        return request;
    }

    private sealed class PaymentFactory : WebApplicationFactory<Program>
    {
        public const string Secret = "payment-api-session-secret-with-enough-entropy";
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "tongyin-pay-api-" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(_folder, "orders.sqlite");

        public RecordingGateway Gateway { get; } = new();
        public RecordingVerifier Verifier { get; } = new();

        public PaymentFactory()
        {
            Directory.CreateDirectory(_folder);
            using var key = RSA.Create(2048);
            File.WriteAllText(Path.Combine(_folder, "merchant.pem"), key.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(Path.Combine(_folder, "platform.pem"), key.ExportSubjectPublicKeyInfoPem());
            using var db = Connect();
            db.Open();
            db.Execute("""
                CREATE TABLE ty_users (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_orders (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_store_lock(id INTEGER PRIMARY KEY); INSERT INTO ty_store_lock(id) VALUES (1);
                """);
            db.Execute("INSERT INTO ty_users(id,owner_id,created_at,payload) VALUES ('owner-1','owner-1','2026-10-17',@Payload)",
                new { Payload = JsonSerializer.Serialize(new { owner = "owner-1", disabled = false }) });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = Secret, ["DB_DRIVER"] = "postgres", ["DB_CONNECTION_STRING"] = "unused-for-test",
                ["DATA_DIR"] = _folder, ["PUBLIC_URL"] = "http://localhost", ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60",
                ["AI_MODEL_DISPATCH_POLL_SECONDS"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
                services.RemoveAll<IWechatAppSettingsStore>();
                services.AddSingleton<IWechatAppSettingsStore, TestWechatSettings>();
                services.RemoveAll<IWechatPayGateway>();
                services.AddSingleton<IWechatPayGateway>(Gateway);
                services.RemoveAll<IWechatPayNotificationVerifier>();
                services.AddSingleton<IWechatPayNotificationVerifier>(Verifier);
                services.RemoveAll<WechatPayConfiguration>();
                services.AddSingleton(new WechatPayConfiguration
                {
                    AppId = "wx-test-app", MchId = "mch-1", MerchantSerial = "merchant-serial",
                    MerchantPrivateKeyPath = Path.Combine(_folder, "merchant.pem"),
                    PlatformPublicKeyPath = Path.Combine(_folder, "platform.pem"),
                    PlatformSerial = "platform-serial",
                    ApiV3Key = "12345678901234567890123456789012",
                    NotifyUrl = "https://api.example.com/api/pay/notify"
                });
                services.RemoveAll<IQueuedImageGenerationReader>();
                services.AddSingleton<IQueuedImageGenerationReader, EmptyQueuedReader>();
            });
        }

        public void SeedOrder()
        {
            using var db = Connect();
            db.Open();
            db.Execute("INSERT INTO ty_orders(id,owner_id,created_at,payload) VALUES ('order-1','owner-1','2026-10-17',@Payload)",
                new { Payload = JsonSerializer.Serialize(new
                {
                    id = "order-1", owner = "owner-1", work_id = "work-1",
                    status = "awaiting_payment", created_at = "2026-10-17T12:00:00+08:00",
                    name = "张三", phone = "13800138000", address = "广州市海珠区新港东路100号",
                    price_fen = 12900, history = new[] { new { at = "2026-10-17T12:00:00+08:00", actor = "customer", action = "created" } }
                }) });
        }
        public string ReadOrder()
        {
            using var db = Connect(); db.Open();
            return db.QuerySingle<string>("SELECT payload FROM ty_orders WHERE id = 'order-1'");
        }
        public string Status() => JsonDocument.Parse(ReadOrder()).RootElement.GetProperty("status").GetString()!;
        public string PaymentStartedAt()
        {
            using var json = JsonDocument.Parse(ReadOrder());
            return json.RootElement.TryGetProperty("payment_started_at", out var v) ? v.GetString() ?? "" : "";
        }
        private DbConnection Connect() => new SqliteConnection($"Data Source={FilePath};Pooling=False");
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }
    }

    private sealed class TestWechatSettings : IWechatAppSettingsStore
    {
        public Task<WechatAppSettings?> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<WechatAppSettings?>(new WechatAppSettings { AppId = "wx-test-app", AppSecret = "" });
        public Task SaveAsync(string appId, string? appSecret, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyQueuedReader : IQueuedImageGenerationReader
    {
        public Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QueuedImageGeneration>>([]);
    }

    private sealed class RecordingGateway : IWechatPayGateway
    {
        public bool Configured { get; set; } = true;
        public bool IsConfigured => Configured;
        public int Calls { get; private set; }
        public string? LastOpenId { get; private set; }
        public string? LastAppId { get; private set; }
        public int LastPriceFen { get; private set; }

        public Task<MiniappPaymentParameters> CreateAsync(OrderSnapshot order, string payerOpenId, string appId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastOpenId = payerOpenId; LastAppId = appId; LastPriceFen = order.PriceFen;
            return Task.FromResult(new MiniappPaymentParameters
            {
                TimeStamp = "1234567890", NonceStr = "nonce-1", Package = "prepay_id=test-prepay", PaySign = "signed"
            });
        }
    }

    private sealed class RecordingVerifier : IWechatPayNotificationVerifier
    {
        public int Calls { get; private set; }
        public bool Reject { get; set; }
        public VerifiedPaymentNotification Result { get; set; } = new()
        {
            AppId = "wx-test-app", MchId = "mch-1", OutTradeNo = "order-1",
            TransactionId = "tx-001", PayerOpenId = "owner-1", TotalFen = 12900
        };
        public VerifiedPaymentNotification? Verify(string body, string timestamp, string nonce, string signature, string serial)
        {
            Calls++;
            if (Reject) throw new InvalidOperationException("验签失败");
            return Result;
        }
    }
}
