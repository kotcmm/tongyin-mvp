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
using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Api.Tests.Works;

/// <summary>从小程序确认单张卡通图开始，端到端验证身份、状态与数据库原子更新。</summary>
public sealed class SelectImageEndpointTests
{
    /// <summary>已生成图片的本人作品被确认后，进入模型待生成状态，保留非业务 JSON 字段。</summary>
    [Fact]
    public async Task SelectImage_ReadyOwnedWork_TransitionsToModelPending()
    {
        using var app = new SelectApiFactory();
        app.SeedUser("owner-1");
        app.SeedWork("work-1", "owner-1", status: "image_ready", candidates: ["one.png", "legacy-two.png"]);
        using var client = app.CreateClient();
        using var request = SelectRequest("owner-1", "work-1", "{\"index\":0}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("model_pending", responseJson.RootElement.GetProperty("status").GetString());
        using var document = JsonDocument.Parse(app.ReadWork("work-1"));
        var root = document.RootElement;
        Assert.Equal("model_pending", root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("selected").GetInt32());
        Assert.Equal("one.png", Assert.Single(root.GetProperty("candidates").EnumerateArray()).GetString());
        Assert.Equal("image-gen-id", root.GetProperty("generation_id").GetString());
        Assert.Equal("保留", root.GetProperty("custom_field").GetString());
        Assert.Equal(string.Empty, root.GetProperty("model").GetString());
        Assert.Empty(root.GetProperty("preview").EnumerateArray());
    }

    /// <summary>确认结果后，作品查询应立即反馈最新模型状态。</summary>
    [Fact]
    public async Task SelectImage_ThenGetWork_ReflectsPendingState()
    {
        using var app = new SelectApiFactory();
        app.SeedUser("owner-1");
        app.SeedWork("work-1", "owner-1", status: "image_ready", candidates: ["one.png"]);
        using var client = app.CreateClient();
        using var request = SelectRequest("owner-1", "work-1", "{\"index\":0}");
        using var accepted = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        using var getRequest = SelectRequest("owner-1", "work-1", body: null);
        using var get = await client.SendAsync(getRequest);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var result = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal("model_pending", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, result.RootElement.GetProperty("selected").GetInt32());
    }

    /// <summary>没有微信会话的请求无法更新作品。</summary>
    [Fact]
    public async Task SelectImage_NoToken_ReturnsUnauthorized()
    {
        using var app = new SelectApiFactory();
        app.SeedWork("work-1", "owner-1", "image_ready", ["one.png"]);
        using var client = app.CreateClient();

        using var response = await client.PostAsync("/api/works/work-1/select", new StringContent("{\"index\":0}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("image_ready", JsonDocument.Parse(app.ReadWork("work-1")).RootElement.GetProperty("status").GetString());
    }

    /// <summary>其他用户与不存在的作品均返回 404，不泄露作品状态。</summary>
    [Theory]
    [InlineData("owner-2", "work-1")]
    [InlineData("owner-1", "nonexistent")]
    public async Task SelectImage_ForeignOrMissing_ReturnsNotFound(string caller, string id)
    {
        using var app = new SelectApiFactory();
        app.SeedUser(caller);
        app.SeedWork("work-1", "owner-1", "image_ready", ["one.png"]);
        using var client = app.CreateClient();
        using var request = SelectRequest(caller, id, "{\"index\":0}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("image_ready", JsonDocument.Parse(app.ReadWork("work-1")).RootElement.GetProperty("status").GetString());
    }

    /// <summary>错误索引、缺少候选图和非就绪状态均不可修改数据。</summary>
    [Theory]
    [InlineData("image_ready", 1, true)]
    [InlineData("image_ready", -1, true)]
    [InlineData("image_ready", 0, false)]
    [InlineData("image_pending", 0, true)]
    [InlineData("model_pending", 0, true)]
    [InlineData("failed", 0, true)]
    public async Task SelectImage_IneligibleWork_ReturnsBadRequestWithoutMutation(string status, int index, bool hasCandidates)
    {
        using var app = new SelectApiFactory();
        app.SeedUser("owner-1");
        app.SeedWork("work-1", "owner-1", status, hasCandidates ? ["one.png"] : []);
        var before = app.ReadWork("work-1");
        using var client = app.CreateClient();
        using var request = SelectRequest("owner-1", "work-1", "{\"index\":" + index + "}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, app.ReadWork("work-1"));
    }

    /// <summary>必须明确提交 JSON 整数索引，不能将缺省 index 当作 0。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"index\":\"0\"}")]
    [InlineData("{\"index\":null}")]
    [InlineData("invalid-json")]
    public async Task SelectImage_InvalidBody_ReturnsBadRequest(string body)
    {
        using var app = new SelectApiFactory();
        app.SeedUser("owner-1");
        app.SeedWork("work-1", "owner-1", "image_ready", ["one.png"]);
        var before = app.ReadWork("work-1");
        using var client = app.CreateClient();
        using var request = SelectRequest("owner-1", "work-1", body);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, app.ReadWork("work-1"));
    }

    private static HttpRequestMessage SelectRequest(string owner, string id, string? body)
    {
        var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, "/api/works/" + id + (body is null ? "" : "/select"));
        var expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var message = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{owner}|{expires}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(SelectApiFactory.Secret), Encoding.UTF8.GetBytes(message));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            message + "." + Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    private sealed class SelectApiFactory : WebApplicationFactory<Program>
    {
        public const string Secret = "select-test-session-secret-with-enough-length";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-select-" + Guid.NewGuid().ToString("N"));
        private string DatabasePath => Path.Combine(_root, "db.sqlite");

        public SelectApiFactory()
        {
            Directory.CreateDirectory(_root);
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                CREATE TABLE ty_works (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_users (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_ai_providers (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_ai_prompts (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_store_lock (id INTEGER PRIMARY KEY);
                INSERT INTO ty_store_lock(id) VALUES (1);
                """;
            command.ExecuteNonQuery();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = Secret,
                ["DB_DRIVER"] = "postgres",
                ["DB_CONNECTION_STRING"] = "unused-in-test",
                ["DATA_DIR"] = _root,
                ["PUBLIC_URL"] = "http://localhost",
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
                services.RemoveAll<IQueuedImageGenerationReader>();
                services.AddSingleton<IQueuedImageGenerationReader, EmptyQueuedReader>();
            });
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DatabasePath};Pooling=False");

        public void SeedUser(string owner)
        {
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO ty_users(id,owner_id,created_at,payload) VALUES (@Id,@Owner,'2026-10-16',@Payload)";
            command.Parameters.Add(new SqliteParameter("@Id", owner));
            command.Parameters.Add(new SqliteParameter("@Owner", owner));
            command.Parameters.Add(new SqliteParameter("@Payload", JsonSerializer.Serialize(new { owner, disabled = false })));
            command.ExecuteNonQuery();
        }

        public void SeedWork(string id, string owner, string status, string[] candidates)
        {
            var value = JsonSerializer.Serialize(new
            {
                id, owner, status, candidates, created_at = "2026-10-16T10:00:00+08:00",
                original = "drawing.png", selected = -1, preview = new[] { "legacy.png" },
                model = "legacy.glb", image_attempts = 1, generation_id = "image-gen-id",
                note = "", custom_field = "保留"
            });
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES (@Id,@Owner,'2026-10-16',@Payload)";
            command.Parameters.Add(new SqliteParameter("@Id", id));
            command.Parameters.Add(new SqliteParameter("@Owner", owner));
            command.Parameters.Add(new SqliteParameter("@Payload", value));
            command.ExecuteNonQuery();
        }

        public string ReadWork(string id)
        {
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT payload FROM ty_works WHERE id=@Id";
            command.Parameters.Add(new SqliteParameter("@Id", id));
            return (string)command.ExecuteScalar()!;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class EmptyQueuedReader : IQueuedImageGenerationReader
    {
        public Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QueuedImageGeneration>>([]);
    }
}
