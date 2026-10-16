using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Tongyin.Api.Tests.Config;

/// <summary>验收小程序公开配置与 Go 旧接口一致，且 AI 可用状态与真实派发条件一致。</summary>
public sealed class MiniappConfigEndpointTests
{
    /// <summary>公开配置无需登录，返回旧小程序直接消费的四个字段，且不包含服务端密钥。</summary>
    [Fact]
    public async Task GetConfig_DefaultValues_ReturnsSafeGoCompatiblePayload()
    {
        using var app = new ConfigFactory();
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal(12900, root.GetProperty("price_fen").GetInt32());
        Assert.Equal(2, root.GetProperty("free_image_attempts").GetInt32());
        Assert.False(root.GetProperty("dev").GetBoolean());
        Assert.False(root.GetProperty("ai_auto_enabled").GetBoolean());
    }

    /// <summary>自定义正整数售价和免费次数应由配置接口按原值返回，而不是固定写死。</summary>
    [Fact]
    public async Task GetConfig_CustomValues_ReturnsConfiguredPriceAndAttempts()
    {
        using var app = new ConfigFactory(price: "7900", attempts: "3");
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(7900, json.RootElement.GetProperty("price_fen").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("free_image_attempts").GetInt32());
    }

    /// <summary>错误、非正数配置不得把价格和免费次数变成 0 或负数。</summary>
    [Theory]
    [InlineData("0", "-1")]
    [InlineData("-10", "0")]
    [InlineData("not-an-integer", "also-invalid")]
    public async Task GetConfig_InvalidNumbers_UsesGoDefaults(string price, string attempts)
    {
        using var app = new ConfigFactory(price: price, attempts: attempts);
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(12900, json.RootElement.GetProperty("price_fen").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("free_image_attempts").GetInt32());
    }

    /// <summary>显式打开开发开关仅在开发环境可向小程序声明开启。</summary>
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task GetConfig_DevFlag_RequiresDevelopmentAndExplicitOptIn(bool development, bool devMode, bool expected)
    {
        using var app = new ConfigFactory(development: development, devMode: devMode);
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("dev").GetBoolean());
    }

    /// <summary>后台停用或启用默认图片服务商后，下一次公开配置请求立即反映状态。</summary>
    [Fact]
    public async Task GetConfig_ProviderEnabledInDatabase_UpdatesWithoutRestart()
    {
        using var app = new ConfigFactory();
        using var client = app.CreateClient();
        using var disabled = await client.GetAsync("/api/config");
        using var initial = JsonDocument.Parse(await disabled.Content.ReadAsStringAsync());
        Assert.False(initial.RootElement.GetProperty("ai_auto_enabled").GetBoolean());

        app.SaveProvider(enabled: true);

        using var enabled = await client.GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        using var updated = JsonDocument.Parse(await enabled.Content.ReadAsStringAsync());
        Assert.True(updated.RootElement.GetProperty("ai_auto_enabled").GetBoolean());
    }

    private sealed class ConfigFactory : WebApplicationFactory<Program>
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-miniapp-config-" + Guid.NewGuid().ToString("N"));
        private readonly string? _price;
        private readonly string? _attempts;
        private readonly bool _development;
        private readonly bool _devMode;
        private string DatabasePath => Path.Combine(_root, "db.sqlite");

        public ConfigFactory(string? price = null, string? attempts = null, bool development = false, bool devMode = false)
        {
            _price = price;
            _attempts = attempts;
            _development = development;
            _devMode = devMode;
            Directory.CreateDirectory(_root);
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                CREATE TABLE ty_ai_providers (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_ai_prompts (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_store_lock (id INTEGER PRIMARY KEY);
                INSERT INTO ty_store_lock(id) VALUES (1);
                """;
            command.ExecuteNonQuery();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_development ? "Development" : "Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "test-session-secret-with-adequate-entropy",
                ["DB_DRIVER"] = "postgres",
                ["DB_CONNECTION_STRING"] = "unused-in-test",
                ["DATA_DIR"] = _root,
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60",
                ["DEV_MODE"] = _devMode ? "true" : "false",
                ["PRODUCT_PRICE_FEN"] = _price,
                ["FREE_IMAGE_ATTEMPTS"] = _attempts
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
            });
        }

        public void SaveProvider(bool enabled)
        {
            var payload = JsonSerializer.Serialize(new
            {
                id = "image-default",
                stage = "image",
                adapter = "seedream",
                endpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations",
                model = "seedream",
                enabled,
                @default = true,
                api_key_cipher = "encrypted-key"
            });
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO ty_ai_providers (id,owner_id,created_at,payload) VALUES ('image-default','system','2026-10-16',@payload)";
            command.Parameters.Add(new SqliteParameter("@payload", payload));
            command.ExecuteNonQuery();
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DatabasePath};Pooling=False");

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
