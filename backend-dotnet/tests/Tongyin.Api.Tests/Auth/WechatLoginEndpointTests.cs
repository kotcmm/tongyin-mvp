using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tongyin.Application.Auth;
using Tongyin.Infrastructure.Auth;
using Xunit;

namespace Tongyin.Api.Tests.Auth;

/// <summary>
/// 从微信临时 code 登录的集成验收，覆盖注册、身份验证与开发环境隔离。
/// </summary>
public sealed class WechatLoginEndpointTests
{
    /// <summary>首次登录须保存用户，且令牌能直接访问受保护的作品接口。</summary>
    [Fact]
    public async Task FirstLogin_CreatesUserAndIssuesWorkingSession()
    {
        using var app = new LoginFactory();
        using var client = app.CreateClient();

        using var response = await client.PostAsync("/api/auth/wechat", JsonContent("valid-code"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.False(json.RootElement.TryGetProperty("session_key", out _));
        Assert.False(json.RootElement.TryGetProperty("openid", out _));
        Assert.Equal(1, app.UserCount("openid-001"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/works");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var works = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, works.StatusCode);
    }

    /// <summary>同一个 OpenID 重复登录不能生成多个用户。</summary>
    [Fact]
    public async Task RepeatedLogin_DoesNotDuplicateUser()
    {
        using var app = new LoginFactory();
        using var client = app.CreateClient();

        using var first = await client.PostAsync("/api/auth/wechat", JsonContent("valid-code"));
        using var second = await client.PostAsync("/api/auth/wechat", JsonContent("valid-code"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, app.UserCount("openid-001"));
    }

    /// <summary>错误或空 code 不允许创建用户。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("invalid-code")]
    public async Task InvalidCode_IsRejectedWithoutCreatingUser(string code)
    {
        using var app = new LoginFactory();
        using var client = app.CreateClient();

        using var response = await client.PostAsync("/api/auth/wechat", JsonContent(code));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.UserCount("openid-001"));
    }

    /// <summary>登录账号已停用时不得颁发有效令牌。</summary>
    [Fact]
    public async Task DisabledUser_IsRejected()
    {
        using var app = new LoginFactory();
        app.SeedUser("openid-001", disabled: true);
        using var client = app.CreateClient();

        using var response = await client.PostAsync("/api/auth/wechat", JsonContent("valid-code"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, app.UserCount("openid-001"));
    }

    /// <summary>服务端已过期的会话令牌不能访问微信用户接口。</summary>
    [Fact]
    public async Task ExpiredSession_CannotAccessProtectedEndpoint()
    {
        using var app = new LoginFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();
        var issuer = app.Services.GetRequiredService<WechatSessionTokenService>();
        var expiredToken = issuer.Issue("openid-001", DateTimeOffset.UtcNow.AddDays(-9), TimeSpan.FromDays(7));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/works");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>生产环境即便 DEV_MODE 为 true 也不允许以 DEV_LOGIN 冒充微信用户。</summary>
    [Fact]
    public async Task Production_DevLoginIsAlwaysRejected()
    {
        using var app = new LoginFactory(production: true, fakeWechat: false, devMode: true);
        using var client = app.CreateClient();

        using var response = await client.PostAsync("/api/auth/wechat", JsonContent("DEV_LOGIN"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.UserCount("dev-openid"));
    }

    /// <summary>开发环境未显式启用 DEV_MODE 时不得使用模拟登录。</summary>
    [Fact]
    public async Task Development_DevLoginRequiresExplicitSetting()
    {
        using var app = new LoginFactory(fakeWechat: false, devMode: false);
        using var client = app.CreateClient();

        using var response = await client.PostAsync("/api/auth/wechat", JsonContent("DEV_LOGIN"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>开发环境明确授权时可以创建固定的本地测试账号。</summary>
    [Fact]
    public async Task Development_ExplicitDevLogin_RegistersLocalUser()
    {
        using var app = new LoginFactory(fakeWechat: false, devMode: true);
        using var client = app.CreateClient();

        using var response = await client.PostAsync("/api/auth/wechat", JsonContent("DEV_LOGIN"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, app.UserCount("dev-openid"));
    }

    private static StringContent JsonContent(string code)
        => new(JsonSerializer.Serialize(new { code }), Encoding.UTF8, "application/json");

    private sealed class LoginFactory : WebApplicationFactory<Program>
    {
        private const string Secret = "test-session-secret-2026-long-enough";
        private readonly bool _production;
        private readonly bool _fakeWechat;
        private readonly bool _devMode;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-login-test-" + Guid.NewGuid().ToString("N"));
        private string DatabasePath => Path.Combine(_root, "db.sqlite");

        public LoginFactory(bool production = false, bool fakeWechat = true, bool devMode = false)
        {
            _production = production;
            _fakeWechat = fakeWechat;
            _devMode = devMode;
            Directory.CreateDirectory(_root);
            using (var db = Connect())
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS ty_store_lock (id INTEGER PRIMARY KEY);
                    INSERT OR IGNORE INTO ty_store_lock(id) VALUES (1);
                    CREATE TABLE IF NOT EXISTS ty_users (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                    CREATE TABLE IF NOT EXISTS ty_works (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                    """;
                cmd.ExecuteNonQuery();
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_production ? "Production" : "Development");
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = Secret,
                ["DB_CONNECTION_STRING"] = "unused",
                ["DB_DRIVER"] = "postgres",
                ["DEV_MODE"] = _devMode ? "true" : "false",
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60",
                ["DATA_DIR"] = _root
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
                if (_fakeWechat)
                {
                    services.RemoveAll<IWechatCodeExchanger>();
                    services.AddScoped<IWechatCodeExchanger>(_ => new FakeWechatCodeExchanger());
                }
            });
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DatabasePath};Pooling=False");

        public int UserCount(string openId)
        {
            using var db = Connect();
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ty_users WHERE owner_id = @owner";
            cmd.Parameters.Add(new SqliteParameter("@owner", openId));
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void SeedUser(string openId, bool disabled)
        {
            using var db = Connect();
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO ty_users(id,owner_id,created_at,payload) VALUES (@id,@owner,@created,@payload)";
            cmd.Parameters.Add(new SqliteParameter("@id", Guid.NewGuid().ToString("N")));
            cmd.Parameters.Add(new SqliteParameter("@owner", openId));
            cmd.Parameters.Add(new SqliteParameter("@created", "2026-10-16T10:00:00+08:00"));
            cmd.Parameters.Add(new SqliteParameter("@payload", JsonSerializer.Serialize(new { owner = openId, disabled })));
            cmd.ExecuteNonQuery();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing: disposing);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeWechatCodeExchanger : IWechatCodeExchanger
    {
        public Task<string> ExchangeAsync(string code, CancellationToken cancellationToken = default)
        {
            if (code == "valid-code") return Task.FromResult("openid-001");
            if (code == "other-code") return Task.FromResult("openid-002");
            throw new ArgumentException("登录凭证无效", nameof(code));
        }
    }
}
