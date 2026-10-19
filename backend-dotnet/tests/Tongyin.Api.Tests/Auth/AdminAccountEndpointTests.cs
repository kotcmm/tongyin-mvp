using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Tongyin.Api.Tests.Auth;

/// <summary>使用真实 Identity、Cookie、CSRF、账号数据库验证后台登录与管理流程。</summary>
public sealed class AdminAccountEndpointTests
{
    private const string User = "adminuser";
    private const string Password = "long-password-original-2026";

    /// <summary>首次登录生成 Identity 密码摘要，Set-Cookie 必须标记为 HttpOnly，不返回 Bearer。</summary>
    [Fact]
    public async Task BootstrapLogin_UsesIdentityHashedPasswordAndHttpOnlyCookie()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        using var anonymous = await client.GetAsync("/api/admin/account");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var login = await LoginAsync(client, User, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.Contains("Tongyin.Admin=", StringComparison.Ordinal)
            && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        var data = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        Assert.False(data.TryGetProperty("token", out _));
        Assert.Equal(User, data.GetProperty("username").GetString());

        var row = app.Stored();
        Assert.Equal(User, row.UserName);
        Assert.NotEqual(Password, row.PasswordHash);
        Assert.StartsWith("AQAAAA", row.PasswordHash, StringComparison.Ordinal);

        using var authenticated = await client.GetAsync("/api/admin/account");
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    /// <summary>错误初始化密码无法创建账号，也不能越权访问后台作品。</summary>
    [Fact]
    public async Task IncorrectBootstrapPassword_DoesNotCreateAdmin()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        using var bad = await LoginAsync(client, User, "invalid-first-password");
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Equal(0, app.Count());

        using var list = await client.GetAsync("/api/admin/works");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    /// <summary>有 Cookie 却没有 CSRF 标头时，修改账号必须拒绝。</summary>
    [Fact]
    public async Task ChangeAccount_RequiresCurrentPasswordAndCsrf()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        using var login = await LoginAsync(client, User, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var missingCsrf = await client.PutAsJsonAsync("/api/admin/account", new
        {
            currentPassword = Password,
            newUsername = "changedadmin"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        using var invalidCurrent = await SendProtectedAsync(client, HttpMethod.Put, "/api/admin/account", new
        {
            currentPassword = "invalid", newUsername = "changedadmin"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCurrent.StatusCode);
        Assert.Equal(User, app.Stored().UserName);
    }

    /// <summary>修改用户名和密码后 Identity 安全戳使旧 Cookie 无效，必须重新登录。</summary>
    [Fact]
    public async Task ChangeUsernamePassword_RevokesIdentityCookieAndUsesNewCredentials()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        using var login = await LoginAsync(client, User, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var changed = await SendProtectedAsync(client, HttpMethod.Put, "/api/admin/account", new
        {
            currentPassword = Password,
            newUsername = "newadmin",
            newPassword = "long-password-updated-2026"
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("newadmin", app.Stored().UserName);
        using var revoked = await client.GetAsync("/api/admin/account");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        using var oldLogin = await LoginAsync(client, User, Password);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using var newLogin = await LoginAsync(client, "newadmin", "long-password-updated-2026");
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    /// <summary>退出后浏览器 Cookie 被清除，并使旧 Cookie 的安全戳失效。</summary>
    [Fact]
    public async Task Logout_InvalidatesIdentitySession()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        using var login = await LoginAsync(client, User, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var logout = await SendProtectedAsync(client, HttpMethod.Post, "/api/admin/account/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var unauthenticated = await client.GetAsync("/api/admin/account");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    /// <summary>旧管理员请求头不能绕过新 Identity Cookie 认证。</summary>
    [Fact]
    public async Task OldAdminTokenAndBearerCannotAccessBackoffice()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        using var legacy = new HttpRequestMessage(HttpMethod.Get, "/api/admin/works");
        legacy.Headers.Add("X-Admin-Token", "any-legacy-secret");
        using var response = await client.SendAsync(legacy);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var bearer = new HttpRequestMessage(HttpMethod.Get, "/api/admin/works");
        bearer.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old-signature");
        using var result = await client.SendAsync(bearer);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password)
        => await SendProtectedAsync(client, HttpMethod.Post, "/api/admin/account/login", new { username, password });

    private static async Task<HttpResponseMessage> SendProtectedAsync(
        HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using var csrf = await client.GetAsync("/api/admin/account/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        using var tokenData = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());
        var token = tokenData.RootElement.GetProperty("token").GetString();
        using var message = new HttpRequestMessage(method, path);
        message.Headers.Add("X-CSRF-TOKEN", token);
        if (body is not null) message.Content = JsonContent.Create(body);
        return await client.SendAsync(message);
    }

    private sealed class AdminFactory : WebApplicationFactory<Program>
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-identity-test-" + Guid.NewGuid().ToString("N"));
        private string DatabasePath => Path.Combine(_root, "identity.db");

        public AdminFactory()
        {
            Directory.CreateDirectory(_root);
            using var db = Connect();
            db.Open();
            db.Execute("""
                CREATE TABLE ty_admin_identity (
                    id TEXT PRIMARY KEY,user_name TEXT NOT NULL,normalized_user_name TEXT NOT NULL UNIQUE,
                    password_hash TEXT NULL,security_stamp TEXT NULL,concurrency_stamp TEXT NULL,
                    access_failed_count INTEGER NOT NULL,lockout_end TEXT NULL,lockout_enabled INTEGER NOT NULL
                );
                """);
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DatabasePath};Pooling=False");

        public int Count()
        {
            using var db = Connect();
            db.Open();
            return db.ExecuteScalar<int>("SELECT COUNT(*) FROM ty_admin_identity");
        }

        public (string UserName, string PasswordHash) Stored()
        {
            using var db = Connect();
            db.Open();
            var row = db.QuerySingle("SELECT user_name, password_hash FROM ty_admin_identity WHERE id='admin'");
            return ((string)row.user_name, (string)row.password_hash);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "identity-test-encryption-secret-is-long-enough",
                ["DB_CONNECTION_STRING"] = "test",
                ["DB_DRIVER"] = "postgres",
                ["ADMIN_BOOTSTRAP_USER"] = User,
                ["ADMIN_BOOTSTRAP_PASSWORD"] = Password,
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60",
                ["AI_MODEL_DISPATCH_POLL_SECONDS"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
