using System.Data.Common;
using System.Net;
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

namespace Tongyin.Api.Tests.Works;

/// <summary>运营后台跨用户作品检查，从读取原画到核对已生成形象与 GLB 模型的接口契约。</summary>
public sealed class AdminWorksEndpointTests
{
    /// <summary>未授权不能查看家长儿童画、候选图与模型，避免公开泄漏媒体签名链接。</summary>
    [Fact]
    public async Task AdminWorks_WithoutToken_Rejects()
    {
        using var app = new WorksFactory();
        app.Seed("work-a", "openid-a", "model_ready");
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/api/admin/works");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>错误管理员令牌和微信 Bearer 会话不能冒用后台身份。</summary>
    [Fact]
    public async Task AdminWorks_WrongToken_Rejects()
    {
        using var app = new WorksFactory();
        app.Seed("work-a", "openid-a", "model_ready");
        using var client = app.CreateClient();
        using var request = Admin("wrong-token");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>管理员能看到不同家长的作品与状态，但响应不得包含 OpenID 或未经签名的裸文件名。</summary>
    [Fact]
    public async Task AdminWorks_TokenListsAcrossOwnersWithSignedSameOriginMedia()
    {
        using var app = new WorksFactory();
        app.Seed("work-a", "openid-a", "image_pending", createdAt: "2026-10-17T09:00:00+08:00");
        app.Seed("work-b", "openid-b", "model_ready", createdAt: "2026-10-18T09:00:00+08:00");
        using var client = app.CreateClient();
        using var response = await client.SendAsync(Admin());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var works = document.RootElement;
        Assert.Equal(2, works.GetArrayLength());
        Assert.Equal("work-b", works[0].GetProperty("id").GetString());
        Assert.Equal("model_ready", works[0].GetProperty("status").GetString());
        Assert.Equal("image_pending", works[1].GetProperty("status").GetString());
        Assert.Equal(-1, works[1].GetProperty("selected").GetInt32());
        var original = works[0].GetProperty("original").GetString()!;
        Assert.StartsWith("/media/original.png?expires=", original);
        Assert.Contains("&signature=", original);
        var candidate = works[0].GetProperty("candidates")[0].GetString()!;
        Assert.StartsWith("/media/cartoon.png?expires=", candidate);
        Assert.Equal(1, works[0].GetProperty("candidates").GetArrayLength());
        Assert.StartsWith("/media/figure.glb?expires=", works[0].GetProperty("model").GetString());
        Assert.False(works[0].TryGetProperty("owner", out _));
        Assert.False(works[1].TryGetProperty("owner_id", out _));
        Assert.DoesNotContain("wrong-origin.example", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("openid-a", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("openid-b", await response.Content.ReadAsStringAsync());
    }

    /// <summary>已签名媒体可以通过已有 /media 入口获取，篡改签名无法访问儿童画。</summary>
    [Fact]
    public async Task AdminWorks_SignedOriginal_IsReadableAndTamperProof()
    {
        using var app = new WorksFactory();
        app.Seed("work-b", "openid-b", "model_ready");
        using var client = app.CreateClient();
        using var response = await client.SendAsync(Admin());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url = document.RootElement[0].GetProperty("original").GetString()!;
        Assert.StartsWith("/media/", url);
        var local = url;
        using var image = await client.GetAsync(local);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        using var forged = await client.GetAsync(local.Replace("signature=", "signature=X"));
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
    }

    /// <summary>.NET 后台增加“作品管理”入口，展示原画、AI 生成图与 GLB 文件。</summary>
    [Fact]
    public async Task AdminPage_RendersWorkManagementNavigation()
    {
        using var app = new WorksFactory();
        using var client = app.CreateClient();
        var html = await client.GetStringAsync("/admin/");
        var script = await client.GetStringAsync("/admin/orders.js");
        Assert.Contains("data-tab=\"works\"", html);
        Assert.Contains("/api/admin/works", script);
        Assert.Contains("原始画作", script);
        Assert.Contains("AI 形象", script);
        Assert.Contains("GLB", script);
        Assert.DoesNotContain("/api/admin/works/asset", script);
    }

    private static HttpRequestMessage Admin(string token = "test-admin-token")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/works");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed class WorksFactory : WebApplicationFactory<Program>
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "tongyin-admin-works-" + Guid.NewGuid().ToString("N"));
        private string DbPath => Path.Combine(_directory, "works.db");

        public WorksFactory()
        {
            Directory.CreateDirectory(Path.Combine(_directory, "media"));
            File.WriteAllBytes(Path.Combine(_directory, "media", "original.png"), [137, 80, 78, 71, 13, 10, 26, 10, 1]);
            using var db = Connect();
            db.Open();
            db.Execute("CREATE TABLE ty_works(id TEXT PRIMARY KEY,owner_id TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL)");
        }

        public void Seed(string id, string owner, string status, string createdAt = "2026-10-17T09:00:00+08:00")
        {
            var payload = JsonSerializer.Serialize(new
            {
                id, owner, created_at = createdAt, original = "original.png",
                candidates = status == "model_ready" ? new[] { "cartoon.png" } : Array.Empty<string>(),
                selected = status == "model_ready" ? 0 : -1,
                preview = Array.Empty<string>(),
                model = status == "model_ready" ? "figure.glb" : "",
                status, generation_id = "job-001", image_attempts = 1
            });
            using var db = Connect();
            db.Open();
            db.Execute("INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES (@Id,@Owner,@CreatedAt,@Payload)",
                new { Id = id, Owner = owner, CreatedAt = createdAt, Payload = payload });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, settings) => settings.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "admin-works-session-secret-with-enough-length",
                ["DB_DRIVER"] = "postgres",
                ["DB_CONNECTION_STRING"] = "unused-for-test",

                ["PUBLIC_URL"] = "https://wrong-origin.example:443",
                ["DATA_DIR"] = _directory,
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60",
                ["AI_MODEL_DISPATCH_POLL_SECONDS"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                Tongyin.Api.Tests.Auth.TestAdminAuthExtensions.AddTestAdminAuthentication(services, "test-admin-token");
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
            });
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DbPath};Pooling=False");

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
}
