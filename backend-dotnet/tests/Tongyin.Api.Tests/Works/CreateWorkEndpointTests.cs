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
using Tongyin.Api.Controllers;
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
    /// 非 multipart 请求应保持旧版 API 的 400 错误，而不是在模型绑定阶段变成 415。
    /// </summary>
    [Fact]
    public async Task PostWork_NonMultipartBody_ReturnsBadRequest()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();
        using var request = Upload("openid-001", Image(), "drawing.png");
        request.Content = new StringContent("not multipart");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.WorkCount());
    }

    /// <summary>
    /// multipart 表单缺少 file 字段时仍然返回 400，不能创建空作品。
    /// </summary>
    [Fact]
    public async Task PostWork_NoFile_ReturnsBadRequest()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();
        using var request = Upload("openid-001", Image(), "drawing.png");
        request.Content = new MultipartFormDataContent();

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.WorkCount());
    }

    /// <summary>
    /// 空文件被绑定到 IFormFile 后也不能创建作品。
    /// </summary>
    [Fact]
    public async Task PostWork_EmptyImage_ReturnsBadRequest()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();

        using var response = await client.SendAsync(Upload("openid-001", Array.Empty<byte>(), "drawing.png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
    public async Task WechatPolicy_RequiresWechatSessionAndEnabledAccount()
    {
        using var app = new WorkApiFactory();
        using var client = app.CreateClient();
        var policies = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var customer = await policies.GetPolicyAsync(AuthorizationPolicies.Wechat);

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

    /// <summary>
    /// 作品列表必须只包含当前用户作品，并为所有媒体资源重新生成签名访问地址。
    /// </summary>
    [Fact]
    public async Task GetWorks_UsesCurrentOwnerAndSignsAllMedia()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        app.SeedWork("work-001", "openid-001", new
        {
            id = "work-001",
            created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png",
            candidates = new[] { "cartoon.png" },
            selected = 0,
            preview = new[] { "angle.png" },
            model = "figure.glb",
            status = "model_ready",
            image_attempts = 2,
            generation_id = "job-001",
            note = "已完成"
        });
        app.SeedWork("work-002", "openid-002", new { id = "work-002", original = "other.png", status = "image_pending" });
        using var client = app.CreateClient();
        using var request = ListRequest("openid-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var work = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("work-001", work.GetProperty("id").GetString());
        Assert.Equal("model_ready", work.GetProperty("status").GetString());
        Assert.Equal(2, work.GetProperty("image_attempts").GetInt32());
        Assert.False(work.TryGetProperty("owner", out _));
        Assert.Contains("/media/drawing.png?expires=", work.GetProperty("original").GetString()!);
        Assert.Contains("/media/cartoon.png?expires=", Assert.Single(work.GetProperty("candidates").EnumerateArray()).GetString()!);
        Assert.Contains("/media/angle.png?expires=", Assert.Single(work.GetProperty("preview").EnumerateArray()).GetString()!);
        Assert.Contains("/media/figure.glb?expires=", work.GetProperty("model").GetString()!);
        Assert.DoesNotContain("other.png", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 已登录用户没有作品时应返回 JSON 空数组。
    /// </summary>
    [Fact]
    public async Task GetWorks_NoWorks_ReturnsEmptyArray()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();
        using var request = ListRequest("openid-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.EnumerateArray());
    }

    /// <summary>
    /// 作品列表需要微信用户登录，不能匿名查询。
    /// </summary>
    [Fact]
    public async Task GetWorks_Anonymous_Rejects()
    {
        using var app = new WorkApiFactory();
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/works");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// 被停用的微信用户不能读取作品列表。
    /// </summary>
    [Fact]
    public async Task GetWorks_DisabledOwner_Rejects()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: true);
        using var client = app.CreateClient();
        using var request = ListRequest("openid-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// 客户专用授权特性必须复用已有微信客户策略，而不能另起认证机制。
    /// </summary>
    [Fact]
    public void WechatAuthorizeAttribute_UsesWechatPolicy()
    {
        var attribute = new WechatAuthorizeAttribute();

        Assert.IsAssignableFrom<AuthorizeAttribute>(attribute);
        Assert.Equal(AuthorizationPolicies.Wechat, attribute.Policy);
    }

    /// <summary>
    /// 作品控制器应只需一个专用特性即可启用客户授权。
    /// </summary>
    [Fact]
    public void WorksController_DeclaresWechatAuthorizeAttribute()
    {
        var attributes = typeof(WorksController).GetCustomAttributes(typeof(WechatAuthorizeAttribute), inherit: true);

        Assert.Single(attributes);
        Assert.IsType<WechatAuthorizeAttribute>(attributes[0]);
    }

    /// <summary>
    /// 查询本人作品详情时必须保留模型状态，并重新签发原画、候选图、预览和 GLB 地址。
    /// </summary>
    [Fact]
    public async Task GetWork_OwnWork_ReturnsSignedDetail()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        app.SeedWork("work-001", "openid-001", new
        {
            id = "work-001",
            created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png",
            candidates = new[] { "cartoon.png" },
            selected = 0,
            preview = new[] { "angle.png" },
            model = "figure.glb",
            status = "model_ready",
            image_attempts = 2,
            generation_id = "job-001",
            note = "已完成"
        });
        using var client = app.CreateClient();
        using var request = WorkDetailRequest("openid-001", "work-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var work = json.RootElement;
        Assert.Equal(JsonValueKind.Object, work.ValueKind);
        Assert.Equal("work-001", work.GetProperty("id").GetString());
        Assert.Equal("model_ready", work.GetProperty("status").GetString());
        Assert.Equal(2, work.GetProperty("image_attempts").GetInt32());
        Assert.Equal("job-001", work.GetProperty("generation_id").GetString());
        Assert.False(work.TryGetProperty("owner", out _));
        Assert.Contains("/media/drawing.png?expires=", work.GetProperty("original").GetString()!);
        Assert.Contains("/media/cartoon.png?expires=", Assert.Single(work.GetProperty("candidates").EnumerateArray()).GetString()!);
        Assert.Contains("/media/angle.png?expires=", Assert.Single(work.GetProperty("preview").EnumerateArray()).GetString()!);
        Assert.Contains("/media/figure.glb?expires=", work.GetProperty("model").GetString()!);
    }

    /// <summary>
    /// 作品存在但不属于当前微信用户时，与作品不存在统一返回 404，不泄露作品是否存在。
    /// </summary>
    [Fact]
    public async Task GetWork_OtherOwner_ReturnsNotFound()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-002", disabled: false);
        app.SeedWork("work-001", "openid-001", new { id = "work-001", original = "private.png", status = "image_pending" });
        using var client = app.CreateClient();
        using var request = WorkDetailRequest("openid-002", "work-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("作品不存在", json.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("private.png", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 不存在的作品 ID 使用旧 Go API 的 404 JSON 响应。
    /// </summary>
    [Fact]
    public async Task GetWork_MissingId_ReturnsNotFound()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: false);
        using var client = app.CreateClient();
        using var request = WorkDetailRequest("openid-001", "missing");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("作品不存在", json.RootElement.GetProperty("error").GetString());
    }

    /// <summary>
    /// 匿名请求不得查询作品详情。
    /// </summary>
    [Fact]
    public async Task GetWork_Anonymous_ReturnsUnauthorized()
    {
        using var app = new WorkApiFactory();
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/works/work-001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// 账号停用后不能读取作品详情。
    /// </summary>
    [Fact]
    public async Task GetWork_DisabledOwner_ReturnsForbidden()
    {
        using var app = new WorkApiFactory();
        app.SeedUser("openid-001", disabled: true);
        using var client = app.CreateClient();
        using var request = WorkDetailRequest("openid-001", "work-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static readonly byte[] _image = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
    private static byte[] Image() => _image;

    private static HttpRequestMessage WorkDetailRequest(string owner, string id)
    {
        var request = Upload(owner, Image(), "drawing.png");
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri($"/api/works/{id}", UriKind.Relative);
        request.Content?.Dispose();
        request.Content = null;
        return request;
    }

    private static HttpRequestMessage ListRequest(string owner)
    {
        var request = Upload(owner, Image(), "drawing.png");
        request.Method = HttpMethod.Get;
        request.Content = null;
        return request;
    }

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

        public void SeedWork(string id, string owner, object payload)
        {
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES (@id,@owner,@created,@payload)";
            command.Parameters.Add(new SqliteParameter("@id", id));
            command.Parameters.Add(new SqliteParameter("@owner", owner));
            command.Parameters.Add(new SqliteParameter("@created", "2026-10-15T10:00:00+08:00"));
            command.Parameters.Add(new SqliteParameter("@payload", JsonSerializer.Serialize(payload)));
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
