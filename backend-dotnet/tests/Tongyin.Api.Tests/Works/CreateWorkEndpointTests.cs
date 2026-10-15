using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Tongyin.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Tongyin.Api.Tests.Works;

/// <summary>
/// 验证原微信小程序创建作品的 HTTP 协议、鉴权与持久化。
/// </summary>
public sealed class CreateWorkEndpointTests
{
    /// <summary>
    /// 有效登录用户上传 PNG 后，必须保存媒体和作品，并返回兼容旧接口的 JSON。
    /// </summary>
    [Fact]
    public async Task PostWork_AuthenticatedImage_PersistsAndReturnsSignedOriginal()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();

        using var response = await client.SendAsync(Upload("openid-001", Image(), "drawing.png"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.Equal("image_pending", body.GetProperty("status").GetString());
        Assert.Equal(-1, body.GetProperty("selected").GetInt32());
        Assert.Equal(0, body.GetProperty("image_attempts").GetInt32());
        Assert.False(body.TryGetProperty("owner", out _));
        var id = body.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id));

        var saved = app.FindWork(id!);
        Assert.Equal("openid-001", saved.Owner);
        Assert.Equal("image_pending", saved.Status);
        var uri = new Uri(body.GetProperty("original").GetString()!);
        Assert.Equal("/media/" + saved.Original, uri.AbsolutePath);
        Assert.True(File.Exists(Path.Combine(app.MediaDirectory, saved.Original)));
        using var mediaResponse = await client.GetAsync(uri.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, mediaResponse.StatusCode);
        Assert.Equal(Image(), await mediaResponse.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// 作品创建响应必须保留原 Go 小程序依赖的全部初始化字段。
    /// </summary>
    [Fact]
    public async Task PostWork_Success_PreservesLegacyResponseFields()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();

        using var response = await client.SendAsync(Upload("openid-001", Image(), "drawing.png"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload = json.RootElement;
        Assert.Equal(JsonValueKind.String, payload.GetProperty("created_at").ValueKind);
        Assert.Empty(payload.GetProperty("candidates").EnumerateArray());
        Assert.Empty(payload.GetProperty("preview").EnumerateArray());
        Assert.Equal(string.Empty, payload.GetProperty("model").GetString());
        Assert.Equal(string.Empty, payload.GetProperty("generation_id").GetString());
        Assert.Equal(string.Empty, payload.GetProperty("note").GetString());
        Assert.Equal("image_pending", payload.GetProperty("status").GetString());
        Assert.False(payload.TryGetProperty("owner", out _));
    }

    /// <summary>
    /// 未携带有效登录信息时，不能上传原画或创建作品。
    /// </summary>
    [Fact]
    public async Task PostWork_MissingToken_RejectsWithoutSaving()
    {
        using var app = new WorkApiFactory();
        using var client = app.CreateClient();
        using var request = Upload("openid-001", Image(), "drawing.png");
        request.Headers.Authorization = null;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, app.WorkCount());
    }

    /// <summary>
    /// 无效的 Bearer 令牌应由统一身份认证流程拒绝，不能创建作品。
    /// </summary>
    [Fact]
    public async Task PostWork_InvalidToken_RejectsWithoutSaving()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();
        using var request = Upload("openid-001", Image(), "drawing.png");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, app.WorkCount());
    }

    /// <summary>
    /// 签名正确但已过期的会话令牌应由统一身份认证流程拒绝。
    /// </summary>
    [Fact]
    public async Task PostWork_ExpiredToken_RejectsWithoutSaving()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();
        using var request = Upload("openid-001", Image(), "drawing.png", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, app.WorkCount());
    }

    /// <summary>
    /// 已停用的微信账号，即使令牌签名合法，也不得创建作品。
    /// </summary>
    [Fact]
    public async Task PostWork_DisabledOwner_RejectsWithoutSaving()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: true);
        using var client = app.CreateClient();

        using var response = await client.SendAsync(Upload("openid-001", Image(), "drawing.png"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, app.WorkCount());
    }

    /// <summary>
    /// 用户数据库里没有身份档案时，不应仅凭签名令牌写入作品。
    /// </summary>
    [Fact]
    public async Task PostWork_UnknownOwner_RejectsWithoutSaving()
    {
        using var app = new WorkApiFactory();
        using var client = app.CreateClient();

        using var response = await client.SendAsync(Upload("openid-001", Image(), "drawing.png"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, app.WorkCount());
    }

    /// <summary>
    /// 非图片内容不得以伪造扩展名上传。
    /// </summary>
    [Fact]
    public async Task PostWork_InvalidImage_Rejects()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();

        using var response = await client.SendAsync(Upload("openid-001", Encoding.UTF8.GetBytes("not-an-image"), "fake.png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.WorkCount());
    }

    /// <summary>
    /// 作品原画上限为 10 MiB，超限不能保存数据。
    /// </summary>
    [Fact]
    public async Task PostWork_OversizedImage_Rejects()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();
        var bytes = new byte[10 * 1024 * 1024 + 1];
        Array.Copy(Image(), bytes, Image().Length);

        using var response = await client.SendAsync(Upload("openid-001", bytes, "big.png"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, app.WorkCount());
    }

    /// <summary>
    /// 数据库写入失败后必须清理已保存的原画，避免孤儿媒体文件。
    /// </summary>
    [Fact]
    public async Task PostWork_DatabaseFailure_RemovesUnreferencedImage()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        app.DropWorks();
        using var client = app.CreateClient();

        using var response = await client.SendAsync(Upload("openid-001", Image(), "drawing.png"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(Directory.GetFiles(app.MediaDirectory));
    }

    /// <summary>
    /// 修改签名后的媒体地址不能被公开访问。
    /// </summary>
    [Fact]
    public async Task GetMedia_InvalidSignature_Rejects()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();
        using var created = await client.SendAsync(Upload("openid-001", Image(), "drawing.png"));
        using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var uri = new Uri(document.RootElement.GetProperty("original").GetString()!);

        using var response = await client.GetAsync(uri.AbsolutePath + "?expires=1&signature=invalid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// 微信客户授权策略必须明确指定微信认证方案和账号启用要求。
    /// </summary>
    [Fact]
    public async Task CustomerPolicy_RequiresWechatSessionAndEnabledAccount()
    {
        using var app = new WorkApiFactory();
        using var client = app.CreateClient();
        var policies = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var customer = await policies.GetPolicyAsync(AuthorizationPolicies.Customer);

        Assert.NotNull(customer);
        Assert.Contains(WechatSessionAuthenticationHandler.SchemeName, customer.AuthenticationSchemes);
        Assert.Contains(customer.Requirements, requirement => requirement is EnabledWechatUserRequirement);
    }

    /// <summary>
    /// 通用默认策略不得绑定微信账号，以免未来管理员认证被误套用客户权限。
    /// </summary>
    [Fact]
    public async Task DefaultPolicy_DoesNotRequireEnabledWechatAccount()
    {
        using var app = new WorkApiFactory();
        using var client = app.CreateClient();
        var policies = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await policies.GetDefaultPolicyAsync();

        Assert.DoesNotContain(fallback.Requirements, requirement => requirement is EnabledWechatUserRequirement);
        Assert.DoesNotContain(WechatSessionAuthenticationHandler.SchemeName, fallback.AuthenticationSchemes);
    }

    private static readonly byte[] _image = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
    private static byte[] Image() => _image;

    private static HttpRequestMessage Upload(string owner, byte[] bytes, string filename, long? expiresAt = null)
    {
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", filename);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/works") { Content = content };
        var exp = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{owner}|{exp}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var sig = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WorkApiFactory.Secret), Encoding.UTF8.GetBytes(payload));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", payload + "." + Convert.ToBase64String(sig).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        return request;
    }

    private sealed class WorkApiFactory : WebApplicationFactory<Program>
    {
        public const string Secret = "test-session-key-with-adequate-entropy";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-api-" + Guid.NewGuid().ToString("N"));
        private string DatabasePath => Path.Combine(_root, "works.db");
        public string MediaDirectory => Path.Combine(_root, "media");

        public WorkApiFactory()
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(MediaDirectory);
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "CREATE TABLE ty_works (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL); CREATE TABLE ty_users (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL)";
            command.ExecuteNonQuery();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = Secret,
                ["DATA_DIR"] = _root,
                ["PUBLIC_URL"] = "http://localhost",
                ["DB_DRIVER"] = "postgres",
                ["DB_CONNECTION_STRING"] = "unused-in-test"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
            });
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DatabasePath};Pooling=False");

        public void SeedUser(string owner, bool disabled)
        {
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO ty_users (id,owner_id,created_at,payload) VALUES ('user-001',@owner,@created,@payload)";
            command.Parameters.Add(new SqliteParameter("@owner", owner));
            command.Parameters.Add(new SqliteParameter("@created", "2026-10-15T10:00:00+08:00"));
            command.Parameters.Add(new SqliteParameter("@payload", JsonSerializer.Serialize(new { owner, disabled })));
            command.ExecuteNonQuery();
        }

        public int WorkCount()
        {
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM ty_works";
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public void DropWorks()
        {
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "DROP TABLE ty_works";
            command.ExecuteNonQuery();
        }

        public (string Owner, string Status, string Original) FindWork(string id)
        {
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT owner_id,payload FROM ty_works WHERE id=@id";
            command.Parameters.Add(new SqliteParameter("@id", id));
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            using var document = JsonDocument.Parse(reader.GetString(1));
            return (reader.GetString(0), document.RootElement.GetProperty("status").GetString()!, document.RootElement.GetProperty("original").GetString()!);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing: disposing);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
