using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

/// <summary>通过完整 HTTP 授权管线和 SQLite 持久化验证后台账号登录与自助维护。</summary>
public sealed class AdminAccountEndpointTests
{
    private const string Username = "adminuser";
    private const string Password = "long-password-original-2026";

    /// <summary>首次成功登录时才创建账号，数据库只能保存带盐散列，管理员令牌请求头不能认证。</summary>
    [Fact]
    public async Task InitialLogin_CreatesHashedAccountAndOnlyBearerCanAccess()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();

        using var anonymous = await client.GetAsync("/api/admin/account");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/admin/account/login", new { username = Username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var data = await login.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(data);
        Assert.Equal(Username, data.Username);
        Assert.False(string.IsNullOrWhiteSpace(data.Token));

        var row = app.Stored();
        Assert.Equal(Username, row.Username);
        Assert.NotEqual(Password, row.PasswordHash);
        Assert.NotEmpty(row.PasswordSalt);
        using var authorized = await client.SendAsync(Admin(HttpMethod.Get, "/api/admin/account", data.Token));
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);

        using var oldHeader = new HttpRequestMessage(HttpMethod.Get, "/api/admin/account");
        oldHeader.Headers.Add("X-Admin-Token", "any-legacy-admin-token");
        using var oldResult = await client.SendAsync(oldHeader);
        Assert.Equal(HttpStatusCode.Unauthorized, oldResult.StatusCode);
    }

    /// <summary>错误用户名或口令不能初始化账号，初始口令不可通过读取请求回显。</summary>
    [Fact]
    public async Task BadBootstrapCredentials_CannotCreateAnAccount()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        using var bad = await client.PostAsJsonAsync("/api/admin/account/login", new { username = Username, password = "incorrect-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Equal(0, app.Count());
    }

    /// <summary>修改账号必须验证现有口令；错误密码不能影响原有管理员会话。</summary>
    [Fact]
    public async Task Update_WrongCurrentPassword_DoesNotChangeCredentials()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        var token = await LoginAsync(client, Username, Password);
        using var update = Admin(HttpMethod.Put, "/api/admin/account", token);
        update.Content = JsonContent.Create(new { currentPassword = "incorrect-password", newUsername = "someoneelse" });

        using var response = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Username, app.Stored().Username);
        using var stillValid = await client.SendAsync(Admin(HttpMethod.Get, "/api/admin/account", token));
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    /// <summary>修改用户名和密码后，旧口令和旧会话都失效，新账号可登录。</summary>
    [Fact]
    public async Task Update_ChangesCredentialsAndRevokesOldSessions()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        var token = await LoginAsync(client, Username, Password);
        using var update = Admin(HttpMethod.Put, "/api/admin/account", token);
        update.Content = JsonContent.Create(new
        {
            currentPassword = Password,
            newUsername = "newadmin",
            newPassword = "long-password-updated-2026"
        });

        using var response = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("newadmin", app.Stored().Username);
        using var oldSession = await client.SendAsync(Admin(HttpMethod.Get, "/api/admin/account", token));
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        using var oldLogin = await client.PostAsJsonAsync("/api/admin/account/login", new { username = Username, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var newToken = await LoginAsync(client, "newadmin", "long-password-updated-2026");
        using var renewed = await client.SendAsync(Admin(HttpMethod.Get, "/api/admin/account", newToken));
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
    }

    /// <summary>主动退出后会话在服务器立即失效，而不仅是浏览器清除状态。</summary>
    [Fact]
    public async Task Logout_RevokesSignedSession()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        var token = await LoginAsync(client, Username, Password);
        using var logout = await client.SendAsync(Admin(HttpMethod.Post, "/api/admin/account/logout", token));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var expired = await client.SendAsync(Admin(HttpMethod.Get, "/api/admin/account", token));
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    /// <summary>旧的初始化配置永远不能覆盖已在后台更新过的账户密码。</summary>
    [Fact]
    public async Task BootstrapSecret_CannotResetExistingAccount()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        var token = await LoginAsync(client, Username, Password);
        using var change = Admin(HttpMethod.Put, "/api/admin/account", token);
        change.Content = JsonContent.Create(new { currentPassword = Password, newPassword = "new-database-password-2026" });
        using var updated = await client.SendAsync(change);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var oldBootstrap = await client.PostAsJsonAsync("/api/admin/account/login", new { username = Username, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldBootstrap.StatusCode);
        _ = await LoginAsync(client, Username, "new-database-password-2026");
    }

    /// <summary>旧版 X-Admin-Token 无法访问包含家长个人订单和儿童作品的后台接口。</summary>
    [Fact]
    public async Task OldAdminToken_CannotAccessProtectedWorks()
    {
        using var app = new AdminFactory();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/works");
        request.Headers.Add("X-Admin-Token", "legacy-secret");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpRequestMessage Admin(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/admin/account/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(payload);
        return payload.Token;
    }

    private sealed class LoginResult
    {
        public string Token { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
    }

    private sealed class AdminFactory : WebApplicationFactory<Program>
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "tongyin-admin-account-" + Guid.NewGuid().ToString("N"));
        private string DbFile => Path.Combine(_directory, "admin.db");

        public AdminFactory()
        {
            Directory.CreateDirectory(_directory);
            using var db = Connect();
            db.Open();
            db.Execute("""
                CREATE TABLE ty_admin_credentials (
                    id INTEGER PRIMARY KEY, username TEXT NOT NULL,
                    password_salt TEXT NOT NULL, password_hash TEXT NOT NULL, session_version TEXT NOT NULL
                );
                """);
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DbFile};Pooling=False");

        public int Count()
        {
            using var db = Connect();
            db.Open();
            return db.ExecuteScalar<int>("SELECT COUNT(*) FROM ty_admin_credentials");
        }

        public (string Username, string PasswordSalt, string PasswordHash) Stored()
        {
            using var db = Connect();
            db.Open();
            return db.QuerySingle<(string Username, string PasswordSalt, string PasswordHash)>(
                "SELECT username AS Username,password_salt AS PasswordSalt,password_hash AS PasswordHash FROM ty_admin_credentials WHERE id=1");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "unit-test-session-secret-adequate-length",
                ["DB_CONNECTION_STRING"] = "unused",
                ["DB_DRIVER"] = "postgres",
                ["ADMIN_BOOTSTRAP_USER"] = Username,
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
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
