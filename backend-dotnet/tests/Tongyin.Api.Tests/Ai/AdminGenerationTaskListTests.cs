using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
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

namespace Tongyin.Api.Tests.Ai;

/// <summary>管理员可查看图片和模型全部生成任务，而不仅是超过十五分钟的异常任务。</summary>
public sealed class AdminGenerationTaskListTests
{
    /// <summary>图片任务包含排队、认领、超时、成功及失败，不混入刚上传尚未申请生成的原画。</summary>
    [Fact]
    public async Task ImageTasks_IncludeFullLifecycleAndExcludeDraft()
    {
        using var app = new TaskFactory();
        app.Seed("draft", "image_pending", generationId: "", attempts: 0);
        app.Seed("queued", "image_pending", attempts: 1, claim: "");
        app.Seed("active", "image_pending", attempts: 1, claim: "job-active", taskStage: "image",
            started: DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), generationId: "job-active");
        app.Seed("stalled", "image_pending", attempts: 1, claim: "job-stalled", taskStage: "image",
            started: DateTimeOffset.UtcNow.AddMinutes(-40).ToString("O"), generationId: "job-stalled");
        app.Seed("done", "image_ready", attempts: 1, candidates: ["cartoon.png"]);
        app.Seed("error", "failed", attempts: 1, note: "图片生成服务连接失败");
        using var client = app.CreateClient();

        using var response = await client.SendAsync(AdminRequest("/api/admin/ai/image-tasks"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.EnumerateArray().ToDictionary(x => x.GetProperty("work_id").GetString()!);
        Assert.Equal(5, items.Count);
        Assert.Equal("queued", items["queued"].GetProperty("status").GetString());
        Assert.False(items["queued"].GetProperty("is_legacy_go").GetBoolean());
        Assert.Equal("processing", items["active"].GetProperty("status").GetString());
        Assert.Equal("stalled", items["stalled"].GetProperty("status").GetString());
        Assert.True(items["stalled"].GetProperty("can_review").GetBoolean());
        Assert.False(items["active"].GetProperty("can_review").GetBoolean());
        Assert.Equal("completed", items["done"].GetProperty("status").GetString());
        Assert.Equal("failed", items["error"].GetProperty("status").GetString());
        Assert.Contains("连接失败", items["error"].GetProperty("note").GetString());
        Assert.DoesNotContain("openid-private", json);
        Assert.DoesNotContain("secret_api_key", json);
    }

    /// <summary>图片完成后转 3D 的作品仍保留图片完成状态；模型队列/认领/成功/失败独立展示。</summary>
    [Fact]
    public async Task ModelTasks_HaveIndependentStatuses_AndImageHistoryRemainsVisible()
    {
        using var app = new TaskFactory();
        app.Seed("model-queued", "model_pending", selected: 0, candidates: ["cartoon.png"], claim: "");
        app.Seed("model-working", "model_pending", selected: 0, candidates: ["cartoon.png"], claim: "job-model",
            taskStage: "model", started: DateTimeOffset.UtcNow.AddMinutes(-3).ToString("O"), generationId: "job-model");
        app.Seed("model-ok", "model_ready", selected: 0, candidates: ["cartoon.png"], model: "figure.glb");
        app.Seed("model-failed", "failed", selected: 0, candidates: ["cartoon.png"], note: "模型服务失败");
        app.Seed("image-only-failed", "failed", attempts: 1, note: "图片失败");
        using var client = app.CreateClient();

        using var modelResponse = await client.SendAsync(AdminRequest("/api/admin/ai/model-tasks"));
        Assert.Equal(HttpStatusCode.OK, modelResponse.StatusCode);
        using var modelDoc = JsonDocument.Parse(await modelResponse.Content.ReadAsStringAsync());
        var models = modelDoc.RootElement.EnumerateArray().ToDictionary(x => x.GetProperty("work_id").GetString()!);
        Assert.Equal(4, models.Count);
        Assert.Equal("queued", models["model-queued"].GetProperty("status").GetString());
        Assert.Equal("processing", models["model-working"].GetProperty("status").GetString());
        Assert.Equal("completed", models["model-ok"].GetProperty("status").GetString());
        Assert.Equal("failed", models["model-failed"].GetProperty("status").GetString());
        Assert.False(models.ContainsKey("image-only-failed"));

        using var images = await client.SendAsync(AdminRequest("/api/admin/ai/image-tasks"));
        using var imageDoc = JsonDocument.Parse(await images.Content.ReadAsStringAsync());
        var completed = imageDoc.RootElement.EnumerateArray().Single(x => x.GetProperty("work_id").GetString() == "model-failed");
        Assert.Equal("completed", completed.GetProperty("status").GetString());
    }

    /// <summary>未经管理员 Cookie 授权，微信用户与匿名请求都不能获取任务进度及故障内容。</summary>
    [Theory]
    [InlineData("image")]
    [InlineData("model")]
    public async Task TaskLists_RequireAdmin(string stage)
    {
        using var app = new TaskFactory();
        app.Seed("private", "image_pending", attempts: 1);
        using var client = app.CreateClient();
        using var anonymous = await client.GetAsync("/api/admin/ai/" + stage + "-tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var forged = new HttpRequestMessage(HttpMethod.Get, "/api/admin/ai/" + stage + "-tasks");
        forged.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        using var denied = await client.SendAsync(forged);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    private static HttpRequestMessage AdminRequest(string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-admin");
        return req;
    }

    private sealed class TaskFactory : WebApplicationFactory<Program>
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-tasks-" + Guid.NewGuid().ToString("N"));
        private string Database => Path.Combine(_root, "tasks.sqlite");
        public TaskFactory()
        {
            Directory.CreateDirectory(_root);
            using var db = Connect();
            db.Open();
            db.Execute("""
                CREATE TABLE ty_works(id TEXT PRIMARY KEY,owner_id TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL);
                CREATE TABLE ty_model_jobs(
                  generation_id TEXT PRIMARY KEY,work_id TEXT NOT NULL,owner_id TEXT,provider_id TEXT NOT NULL,
                  prompt_version INTEGER NOT NULL,provider_job_id TEXT,status TEXT NOT NULL,
                  created_at TEXT NOT NULL,updated_at TEXT NOT NULL,api_key_cipher TEXT,model TEXT);
                """);
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={Database};Pooling=False");

        public void Seed(string id, string status, string generationId = "job-001", int attempts = 1,
            string claim = "", string taskStage = "", string started = "", int selected = -1,
            string[]? candidates = null, string model = "", string note = "")
        {
            using var db = Connect();
            db.Open();
            var json = JsonSerializer.Serialize(new
            {
                id, owner = "openid-private", status, created_at = "2026-10-19T10:00:00+08:00",
                generation_id = generationId, image_attempts = attempts,
                ai_execution_claim_id = claim, ai_task_stage = taskStage, ai_started_at = started,
                ai_provider_id = "seedream-001", ai_prompt_version = 3, selected,
                original = "original.png", candidates = candidates ?? [], model, note
            });
            db.Execute("INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES(@Id,@Owner,@Time,@Payload)",
                new { Id = id, Owner = "openid-private", Time = "2026-10-19T10:00:00+08:00", Payload = json });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_DRIVER"] = "postgres", ["DB_CONNECTION_STRING"] = "test",
                ["SESSION_SECRET"] = "generation-task-test-secret-long-enough",
                ["DATA_DIR"] = _root, ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60",
                ["AI_MODEL_DISPATCH_POLL_SECONDS"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                Tongyin.Api.Tests.Auth.TestAdminAuthExtensions.AddTestAdminAuthentication(services, "test-admin");
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
