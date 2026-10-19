using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
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
using Xunit;

namespace Tongyin.Api.Tests.Works;

/// <summary>微信用户删除作品的 HTTP、登录态、归属、订单与排队安全边界验收。</summary>
public sealed class DeleteWorkEndpointTests
{
    /// <summary>未登录时不能删除其他用户的作品。</summary>
    [Fact]
    public async Task Delete_Unauthenticated_Returns401()
    {
        using var app = new DeleteFactory();
        app.SeedWork("work-001", "openid-001");
        using var client = app.CreateClient();

        using var response = await client.DeleteAsync("/api/works/work-001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, app.CountWorks());
    }

    /// <summary>删除自己的作品返回 204，随后作品列表和详情均无法访问。</summary>
    [Fact]
    public async Task Delete_OwnWork_RemovesFromMyWorksAndDetails()
    {
        using var app = new DeleteFactory();
        app.SeedUser("openid-001");
        app.SeedWork("work-001", "openid-001", "failed");
        app.SeedWork("work-002", "openid-001", "model_ready");
        using var client = app.CreateClient();
        var token = Token("openid-001");

        using var deleted = await client.SendAsync(Authorized(HttpMethod.Delete, "/api/works/work-001", token));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(1, app.CountWorks());

        using var detail = await client.SendAsync(Authorized(HttpMethod.Get, "/api/works/work-001", token));
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        using var list = await client.SendAsync(Authorized(HttpMethod.Get, "/api/works", token));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var works = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal("work-002", Assert.Single(works.RootElement.EnumerateArray()).GetProperty("id").GetString());
    }

    /// <summary>作品不能被其他账号删除，隐藏目标是否真实存在。</summary>
    [Fact]
    public async Task Delete_ForeignWork_Returns404AndKeepsOriginal()
    {
        using var app = new DeleteFactory();
        app.SeedUser("openid-002");
        app.SeedWork("work-001", "openid-001", "model_ready");
        using var client = app.CreateClient();

        using var response = await client.SendAsync(
            Authorized(HttpMethod.Delete, "/api/works/work-001", Token("openid-002")));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, app.CountWorks());
    }

    /// <summary>存在已创建订单时返回 409，确保付款和履约订单不丢失其作品关联。</summary>
    [Fact]
    public async Task Delete_OrderLinked_Returns409WithReadableMessage()
    {
        using var app = new DeleteFactory();
        app.SeedUser("openid-001");
        app.SeedWork("work-001", "openid-001", "model_ready");
        app.SeedOrder("order-001", "work-001");
        using var client = app.CreateClient();

        using var response = await client.SendAsync(
            Authorized(HttpMethod.Delete, "/api/works/work-001", Token("openid-001")));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("已有订单", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, app.CountWorks());
    }

    /// <summary>已经提交的 AI 任务不允许删除，但未认领的 .NET 排队任务可以取消。</summary>
    [Fact]
    public async Task Delete_ClaimedJobBlocked_QueuedJobAllowed()
    {
        using var app = new DeleteFactory();
        app.SeedUser("openid-001");
        app.SeedWork("claimed", "openid-001", "image_pending", claim: "job-001");
        app.SeedWork("queued", "openid-001", "image_pending", claim: "");
        using var client = app.CreateClient();

        using var blocked = await client.SendAsync(
            Authorized(HttpMethod.Delete, "/api/works/claimed", Token("openid-001")));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var allowed = await client.SendAsync(
            Authorized(HttpMethod.Delete, "/api/works/queued", Token("openid-001")));
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal(1, app.CountWorks());
    }

    /// <summary>已删除的作品再次删除应返回 404，不产生重复处理。</summary>
    [Fact]
    public async Task Delete_AlreadyDeleted_Returns404()
    {
        using var app = new DeleteFactory();
        app.SeedUser("openid-001");
        app.SeedWork("work-001", "openid-001", "failed");
        using var client = app.CreateClient();

        using var first = await client.SendAsync(
            Authorized(HttpMethod.Delete, "/api/works/work-001", Token("openid-001")));
        using var second = await client.SendAsync(
            Authorized(HttpMethod.Delete, "/api/works/work-001", Token("openid-001")));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string route, string token)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string Token(string owner)
    {
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            owner + "|" + DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(DeleteFactory.Secret), Encoding.UTF8.GetBytes(data));
        return data + "." + Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class DeleteFactory : WebApplicationFactory<Program>
    {
        public const string Secret = "delete-works-integration-test-secret-is-long-enough";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-delete-" + Guid.NewGuid().ToString("N"));
        private string FileName => Path.Combine(_root, "works.db");

        public DeleteFactory()
        {
            Directory.CreateDirectory(_root);
            using var db = Connect();
            db.Open();
            db.Execute("""
                CREATE TABLE ty_store_lock (id INTEGER PRIMARY KEY);
                INSERT INTO ty_store_lock(id) VALUES (1);
                CREATE TABLE ty_works(id TEXT PRIMARY KEY,owner_id TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL);
                CREATE TABLE ty_orders(id TEXT PRIMARY KEY,owner_id TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL);
                CREATE TABLE ty_users(id TEXT PRIMARY KEY,owner_id TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL);
                """);
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={FileName};Pooling=False");

        public int CountWorks()
        {
            using var db = Connect();
            db.Open();
            return db.ExecuteScalar<int>("SELECT COUNT(*) FROM ty_works");
        }

        public void SeedUser(string owner)
        {
            using var db = Connect();
            db.Open();
            db.Execute(
                "INSERT INTO ty_users(id,owner_id,created_at,payload) VALUES(@Id,@Owner,@Created,@Payload)",
                new { Id = "user-" + owner, Owner = owner, Created = "2026-10-19T10:00:00+08:00",
                    Payload = JsonSerializer.Serialize(new { owner, disabled = false }) });
        }

        public void SeedWork(string id, string owner, string status = "failed", string claim = "")
        {
            using var db = Connect();
            db.Open();
            var payload = JsonSerializer.Serialize(new
            {
                id, owner, status, created_at = "2026-10-19T10:00:00+08:00",
                original = "original.png", candidates = Array.Empty<string>(), preview = Array.Empty<string>(),
                selected = -1, model = "", generation_id = "job-001",
                ai_execution_claim_id = claim, ai_task_stage = "", ai_started_at = "",
                image_attempts = 1, note = ""
            });
            db.Execute("INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES(@Id,@Owner,@Created,@Payload)",
                new { Id = id, Owner = owner, Created = "2026-10-19T10:00:00+08:00", Payload = payload });
        }

        public void SeedOrder(string id, string workId)
        {
            using var db = Connect();
            db.Open();
            db.Execute("INSERT INTO ty_orders(id,owner_id,created_at,payload) VALUES(@Id,@Owner,@Created,@Payload)",
                new { Id = id, Owner = "openid-001", Created = "2026-10-19T10:00:00+08:00",
                    Payload = JsonSerializer.Serialize(new { id, work_id = workId, status = "awaiting_payment" }) });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SESSION_SECRET"] = Secret,
                    ["DB_DRIVER"] = "postgres",
                    ["DB_CONNECTION_STRING"] = "unused-test",
                    ["DATA_DIR"] = _root,
                    ["PUBLIC_URL"] = "http://localhost:18080",
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
