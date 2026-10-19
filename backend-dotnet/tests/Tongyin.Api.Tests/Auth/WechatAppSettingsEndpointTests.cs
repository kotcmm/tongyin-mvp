using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tongyin.Application.Auth;
using Xunit;

namespace Tongyin.Api.Tests.Auth;

/// <summary>微信应用设置只能通过管理员授权写入数据库，所有读接口均不得回显 AppSecret。</summary>
public sealed class WechatAppSettingsEndpointTests
{
    /// <summary>未授权者不能查看或修改微信小程序凭据。</summary>
    [Fact]
    public async Task ReadAndWrite_WithoutAdminSession_ReturnUnauthorized()
    {
        using var app = new SettingsFactory();
        using var client = app.CreateClient();
        using var get = await client.GetAsync("/api/admin/system/wechat");
        using var put = await client.PutAsJsonAsync("/api/admin/system/wechat",
            new { app_id = "wx-test-id", app_secret = "new-test-secret" });
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Null(await app.ReadSettingsAsync());
    }

    /// <summary>管理员保存后能查看 AppID 和配置状态，绝不返回 AppSecret 或密文。</summary>
    [Fact]
    public async Task Save_ThenReadStatus_HidesSecret()
    {
        using var app = new SettingsFactory();
        using var client = app.CreateClient();
        using var put = AdminPut(new { app_id = "wx-test-id", app_secret = "super-private-value" });
        using var saved = await client.SendAsync(put);
        using var get = AdminGet();
        using var status = await client.SendAsync(get);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var data = await status.Content.ReadAsStringAsync();
        Assert.Contains("wx-test-id", data);
        Assert.Contains("\"has_app_secret\":true", data);
        Assert.DoesNotContain("super-private-value", data);
        Assert.DoesNotContain("\"app_secret\":", data);
        var stored = app.ReadDatabasePayload();
        Assert.DoesNotContain("super-private-value", stored);
    }

    /// <summary>管理员更新 AppID 时不填新密钥，应保留数据库中原有加密密钥。</summary>
    [Fact]
    public async Task Save_WithoutNewSecret_PreservesExistingSecret()
    {
        using var app = new SettingsFactory();
        using var client = app.CreateClient();
        using var original = AdminPut(new { app_id = "wx-first", app_secret = "original-secret" });
        using var first = await client.SendAsync(original);
        using var changed = AdminPut(new { app_id = "wx-second", app_secret = "" });
        using var second = await client.SendAsync(changed);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var settings = await app.ReadSettingsAsync();
        Assert.Equal("wx-second", settings?.AppId);
        Assert.Equal("original-secret", settings?.AppSecret);
    }

    /// <summary>修改微信配置后，新的微信登录请求立即使用新值，不依赖服务器环境变量或重启。</summary>
    [Fact]
    public async Task UpdatedDatabaseSettings_AreImmediatelyVisibleToLoginClient()
    {
        using var app = new SettingsFactory();
        using var client = app.CreateClient();
        using var original = AdminPut(new { app_id = "wx-old", app_secret = "old-secret" });
        using var first = await client.SendAsync(original);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var originalSettings = await app.ReadSettingsAsync();
        using var changed = AdminPut(new { app_id = "wx-new", app_secret = "new-secret" });
        using var second = await client.SendAsync(changed);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var latest = await app.ReadSettingsAsync();
        Assert.Equal("wx-old", originalSettings?.AppId);
        Assert.Equal("wx-new", latest?.AppId);
        Assert.Equal("new-secret", latest?.AppSecret);
    }

    /// <summary>管理员不可以把 AppID 保存为空值，阻断不可用配置。</summary>
    [Fact]
    public async Task EmptyAppId_IsRejected()
    {
        using var app = new SettingsFactory();
        using var client = app.CreateClient();
        using var request = AdminPut(new { app_id = "", app_secret = "some-secret" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpRequestMessage AdminPut(object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/system/wechat")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-admin-token");
        return request;
    }

    private static HttpRequestMessage AdminGet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/system/wechat");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-admin-token");
        return request;
    }

    private sealed class SettingsFactory : WebApplicationFactory<Program>
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-wxsettings-" + Guid.NewGuid().ToString("N"));
        private string DatabasePath => Path.Combine(_root, "db.sqlite");

        public SettingsFactory()
        {
            Directory.CreateDirectory(_root);
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                CREATE TABLE ty_system_settings(id TEXT PRIMARY KEY, payload TEXT NOT NULL);
                CREATE TABLE ty_store_lock(id INTEGER PRIMARY KEY);
                INSERT INTO ty_store_lock (id) VALUES (1);
                """;
            command.ExecuteNonQuery();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "test-server-key-with-adequate-entropy",
                ["DB_DRIVER"] = "postgres",
                ["DB_CONNECTION_STRING"] = "unused",
                ["ADMIN_TOKEN"] = "test-admin-token",
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60",
                ["DATA_DIR"] = _root
            }));
            builder.ConfigureTestServices(services =>
            {
                Tongyin.Api.Tests.Auth.TestAdminAuthExtensions.AddTestAdminAuthentication(services, "test-admin-token");
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
            });
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DatabasePath};Pooling=False");

        public async Task<WechatAppSettings?> ReadSettingsAsync()
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IWechatAppSettingsStore>().GetAsync();
        }

        public string ReadDatabasePayload()
        {
            using var db = Connect();
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT payload FROM ty_system_settings WHERE id = 'wechat_miniapp'";
            return (string)cmd.ExecuteScalar()!;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
