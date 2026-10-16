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

/// <summary>
/// 验证微信用户提交图片生成任务的 HTTP 边界和数据库原子登记，不调用实际收费模型。
/// </summary>
public sealed class GenerateImageEndpointTests
{
    /// <summary>
    /// 服务可用且作品属于当前用户时，应接收任务并保存生成编号与首次尝试次数。
    /// </summary>
    [Fact]
    public async Task GenerateImage_OwnWork_RegistersAndReturnsAccepted()
    {
        using var app = new GenerateImageApiFactory(enabled: true);
        app.SeedUser("openid-001");
        app.SeedWork("work-001", "openid-001");
        using var client = app.CreateClient();
        using var request = GenerateRequest("openid-001", "work-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("image_pending", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, result.RootElement.GetProperty("image_attempts").GetInt32());
        var generationId = result.RootElement.GetProperty("generation_id").GetString();
        Assert.True(Guid.TryParseExact(generationId, "N", out _));
        Assert.False(result.RootElement.TryGetProperty("owner", out _));

        using var stored = JsonDocument.Parse(app.ReadWorkPayload("work-001"));
        Assert.Equal(generationId, stored.RootElement.GetProperty("generation_id").GetString());
        Assert.Equal(1, stored.RootElement.GetProperty("image_attempts").GetInt32());
        Assert.Equal(string.Empty, stored.RootElement.GetProperty("ai_execution_claim_id").GetString());
        Assert.Equal("image_pending", stored.RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    /// 作品生成任务尚未完成时再次提交，必须拒绝且不能额外消耗生成次数。
    /// </summary>
    [Fact]
    public async Task GenerateImage_WhilePending_RejectsWithoutConsumingAnotherAttempt()
    {
        using var app = new GenerateImageApiFactory(enabled: true);
        app.SeedUser("openid-001");
        app.SeedWork("work-001", "openid-001");
        using var client = app.CreateClient();
        using var firstRequest = GenerateRequest("openid-001", "work-001");
        using var first = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        using var initial = JsonDocument.Parse(app.ReadWorkPayload("work-001"));
        var generationId = initial.RootElement.GetProperty("generation_id").GetString();

        using var secondRequest = GenerateRequest("openid-001", "work-001");
        using var second = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var stored = JsonDocument.Parse(app.ReadWorkPayload("work-001"));
        Assert.Equal(1, stored.RootElement.GetProperty("image_attempts").GetInt32());
        Assert.Equal(generationId, stored.RootElement.GetProperty("generation_id").GetString());
    }

    /// <summary>
    /// 跨用户提交已有作品时不应透露其存在，也不得修改图片任务数据。
    /// </summary>
    [Fact]
    public async Task GenerateImage_ForeignWork_ReturnsNotFound()
    {
        using var app = new GenerateImageApiFactory(enabled: true);
        app.SeedUser("openid-002");
        app.SeedWork("work-001", "openid-001");
        using var client = app.CreateClient();
        using var request = GenerateRequest("openid-002", "work-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var stored = JsonDocument.Parse(app.ReadWorkPayload("work-001"));
        Assert.Equal(0, stored.RootElement.GetProperty("image_attempts").GetInt32());
        Assert.Equal(string.Empty, stored.RootElement.GetProperty("generation_id").GetString());
    }

    /// <summary>
    /// 微信用户未认证时不能创建任何生成任务。
    /// </summary>
    [Fact]
    public async Task GenerateImage_WithoutWechatSession_ReturnsUnauthorized()
    {
        using var app = new GenerateImageApiFactory(enabled: true);
        app.SeedWork("work-001", "openid-001");
        using var client = app.CreateClient();

        using var response = await client.PostAsync("/api/works/work-001/generate", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var stored = JsonDocument.Parse(app.ReadWorkPayload("work-001"));
        Assert.Equal(0, stored.RootElement.GetProperty("image_attempts").GetInt32());
    }

    /// <summary>
    /// 未授权开启付费 AI 派发时不能先扣除用户次数再无限期挂起任务。
    /// </summary>
    [Fact]
    public async Task GenerateImage_DispatchDisabled_RejectsBeforeRegistration()
    {
        using var app = new GenerateImageApiFactory(enabled: false);
        app.SeedUser("openid-001");
        app.SeedWork("work-001", "openid-001");
        using var client = app.CreateClient();
        using var request = GenerateRequest("openid-001", "work-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var stored = JsonDocument.Parse(app.ReadWorkPayload("work-001"));
        Assert.Equal(0, stored.RootElement.GetProperty("image_attempts").GetInt32());
        Assert.Equal(string.Empty, stored.RootElement.GetProperty("generation_id").GetString());
    }

    private static HttpRequestMessage GenerateRequest(string owner, string id)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/works/{id}/generate");
        var expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{owner}|{expires}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(GenerateImageApiFactory.Secret), Encoding.UTF8.GetBytes(payload));
        var token = payload + "." + Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed class GenerateImageApiFactory : WebApplicationFactory<Program>
    {
        public const string Secret = "test-session-key-with-adequate-entropy";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-generate-api-" + Guid.NewGuid().ToString("N"));
        private readonly bool _enabled;
        private string DatabasePath => Path.Combine(_root, "work.db");

        public GenerateImageApiFactory(bool enabled)
        {
            _enabled = enabled;
            Directory.CreateDirectory(_root);
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                CREATE TABLE ty_works (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_users (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_store_lock (id INTEGER PRIMARY KEY);
                INSERT INTO ty_store_lock (id) VALUES (1);
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
                ["AI_IMAGE_DISPATCH_ENABLED"] = _enabled ? "true" : "false",
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
            command.CommandText = "INSERT INTO ty_users (id, owner_id, created_at, payload) VALUES (@Id, @Owner, @CreatedAt, @Payload)";
            command.Parameters.Add(new SqliteParameter("@Id", "user-" + owner));
            command.Parameters.Add(new SqliteParameter("@Owner", owner));
            command.Parameters.Add(new SqliteParameter("@CreatedAt", "2026-10-16T10:00:00+08:00"));
            command.Parameters.Add(new SqliteParameter("@Payload", JsonSerializer.Serialize(new { owner, disabled = false })));
            command.ExecuteNonQuery();
        }

        public void SeedWork(string id, string owner)
        {
            var payload = JsonSerializer.Serialize(new
            {
                id, owner, created_at = "2026-10-16T10:00:00+08:00", original = "drawing.png",
                candidates = Array.Empty<string>(), selected = -1, preview = Array.Empty<string>(), model = "",
                status = "image_pending", image_attempts = 0, generation_id = "", note = ""
            });
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO ty_works (id, owner_id, created_at, payload) VALUES (@Id, @Owner, @CreatedAt, @Payload)";
            command.Parameters.Add(new SqliteParameter("@Id", id));
            command.Parameters.Add(new SqliteParameter("@Owner", owner));
            command.Parameters.Add(new SqliteParameter("@CreatedAt", "2026-10-16T10:00:00+08:00"));
            command.Parameters.Add(new SqliteParameter("@Payload", payload));
            command.ExecuteNonQuery();
        }

        public string ReadWorkPayload(string id)
        {
            using var db = Connect();
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT payload FROM ty_works WHERE id = @Id";
            command.Parameters.Add(new SqliteParameter("@Id", id));
            return (string)command.ExecuteScalar()!;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing: disposing);
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class EmptyQueuedReader : IQueuedImageGenerationReader
    {
        public Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QueuedImageGeneration>>([]);
    }
}
