using System.Buffers.Binary;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
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

namespace Tongyin.Api.Tests.Ai;

/// <summary>3D 模型超时任务的运营核查，包括授权、准确任务匹配、GLB 补录与审计。</summary>
public sealed class AdminModelRecoveryEndpointTests
{
    private const string Tasks = "/api/admin/ai/stalled-model-tasks";
    private const string WorkId = "work-model";
    private const string GenerationId = "generation-model";

    /// <summary>匿名请求不能读取其他家长的模型任务及相关元数据。</summary>
    [Fact]
    public async Task List_Anonymous_Rejects()
    {
        using var app = new ModelFactory();
        app.Seed(WorkId);
        using var client = app.CreateClient();
        using var result = await client.GetAsync(Tasks);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    /// <summary>只列出超过 15 分钟已认领的模型任务，忽略未开始、未超时及错误阶段。</summary>
    [Fact]
    public async Task List_WithAdminSession_FiltersUnfinishedClaimedModels()
    {
        using var app = new ModelFactory();
        app.Seed(WorkId, owner: "openid-a");
        app.Seed("work-new", owner: "openid-b", startedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        app.Seed("work-queued", owner: "openid-c", claimed: false);
        app.Seed("work-image", owner: "openid-d", stage: "image");
        using var client = app.CreateClient();
        using var result = await client.SendAsync(Admin(HttpMethod.Get, Tasks));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        var task = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(WorkId, task.GetProperty("work_id").GetString());
        Assert.Equal(GenerationId, task.GetProperty("generation_id").GetString());
        Assert.Equal(1, task.GetProperty("image_attempts").GetInt32());
        Assert.DoesNotContain("openid-", json.RootElement.ToString());
    }

    /// <summary>核实失败后必须保留已选卡通图、写入失败原因与 Go 兼容审计。</summary>
    [Fact]
    public async Task Fail_Confirmed_UpdatesStatusAndAudit()
    {
        using var app = new ModelFactory();
        app.Seed(WorkId);
        using var client = app.CreateClient();
        using var request = Admin(HttpMethod.Post, Tasks + "/" + WorkId + "/fail");
        request.Content = JsonContent.Create(new { generation_id = GenerationId, reason = "已核查模型服务无结果" });
        using var result = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var work = JsonDocument.Parse(app.Work(WorkId));
        Assert.Equal("failed", work.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, work.RootElement.GetProperty("selected").GetInt32());
        Assert.Equal("cartoon.png", work.RootElement.GetProperty("candidates")[0].GetString());
        Assert.Equal("保留", work.RootElement.GetProperty("custom_field").GetString());
        Assert.Contains("模型生成失败", work.RootElement.GetProperty("note").GetString());
        using var audit = JsonDocument.Parse(Assert.Single(app.Audits()));
        Assert.Equal("ai.model.review.failed", audit.RootElement.GetProperty("action").GetString());
        Assert.Equal("admin", audit.RootElement.GetProperty("owner").GetString());
        using var detail = JsonDocument.Parse(audit.RootElement.GetProperty("detail").GetString()!);
        Assert.Equal(GenerationId, detail.RootElement.GetProperty("generation_id").GetString());
        Assert.Equal("已核查模型服务无结果", detail.RootElement.GetProperty("reason").GetString());
    }

    /// <summary>上传合法 GLB 后模型可通过签名媒体链接下载，且成功审计与作品更新一并提交。</summary>
    [Fact]
    public async Task Complete_ActualGlb_PersistsAndServesSignedModel()
    {
        using var app = new ModelFactory();
        app.Seed(WorkId);
        using var client = app.CreateClient();
        using var request = Admin(HttpMethod.Post, Tasks + "/" + WorkId + "/complete");
        request.Content = Upload(ValidGlb());
        using var result = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var work = JsonDocument.Parse(app.Work(WorkId));
        Assert.Equal("model_ready", work.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, work.RootElement.GetProperty("selected").GetInt32());
        Assert.Equal("cartoon.png", work.RootElement.GetProperty("candidates")[0].GetString());
        var model = work.RootElement.GetProperty("model").GetString()!;
        Assert.EndsWith(".glb", model);
        Assert.Equal(ValidGlb(), File.ReadAllBytes(Path.Combine(app.DirectoryPath, "media", model)));
        using var listing = await client.SendAsync(Admin(HttpMethod.Get, "/api/admin/works"));
        using var data = JsonDocument.Parse(await listing.Content.ReadAsStringAsync());
        var signed = data.RootElement[0].GetProperty("model").GetString()!;
        Assert.Contains("/media/" + model + "?expires=", signed);
        Assert.StartsWith("/media/", signed);
        using var downloaded = await client.GetAsync(signed);
        Assert.Equal(ValidGlb(), await downloaded.Content.ReadAsByteArrayAsync());
        using var audit = JsonDocument.Parse(Assert.Single(app.Audits()));
        Assert.Equal("ai.model.review.completed", audit.RootElement.GetProperty("action").GetString());
    }

    /// <summary>过期任务编号不能将新版任务误标记失败或生成模型。</summary>
    [Fact]
    public async Task Fail_StaleGeneration_ReturnsConflictWithoutAudit()
    {
        using var app = new ModelFactory();
        app.Seed(WorkId);
        using var client = app.CreateClient();
        using var request = Admin(HttpMethod.Post, Tasks + "/" + WorkId + "/fail");
        request.Content = JsonContent.Create(new { generation_id = "outdated", reason = "已核实" });
        using var result = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("model_pending", JsonDocument.Parse(app.Work(WorkId)).RootElement.GetProperty("status").GetString());
        Assert.Empty(app.Audits());
    }

    /// <summary>任务刚开始不能被人工提前结束，避免与正在执行的 3D Worker 竞争。</summary>
    [Fact]
    public async Task Fail_NotStalled_ReturnsConflict()
    {
        using var app = new ModelFactory();
        app.Seed(WorkId, startedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = app.CreateClient();
        using var request = Admin(HttpMethod.Post, Tasks + "/" + WorkId + "/fail");
        request.Content = JsonContent.Create(new { generation_id = GenerationId, reason = "尚未核实" });
        using var result = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Empty(app.Audits());
    }

    /// <summary>上传伪装成 .glb 的任意字节必须拒绝，不能留下文件或写入审计。</summary>
    [Fact]
    public async Task Complete_InvalidGlb_RejectsAndCleansUp()
    {
        using var app = new ModelFactory();
        app.Seed(WorkId);
        using var client = app.CreateClient();
        using var request = Admin(HttpMethod.Post, Tasks + "/" + WorkId + "/complete");
        request.Content = Upload(Encoding.UTF8.GetBytes("invalid file"));
        using var result = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("model_pending", JsonDocument.Parse(app.Work(WorkId)).RootElement.GetProperty("status").GetString());
        Assert.Empty(app.Audits());
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(app.DirectoryPath, "media")));
    }

    /// <summary>重复上传旧任务时，数据库冲突不能留下未关联的 GLB 文件。</summary>
    [Fact]
    public async Task Complete_SecondUpload_RejectsAndDeletesOrphan()
    {
        using var app = new ModelFactory();
        app.Seed(WorkId);
        using var client = app.CreateClient();
        using var first = Admin(HttpMethod.Post, Tasks + "/" + WorkId + "/complete");
        first.Content = Upload(ValidGlb());
        using var done = await client.SendAsync(first);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        using var again = Admin(HttpMethod.Post, Tasks + "/" + WorkId + "/complete");
        again.Content = Upload(ValidGlb());
        using var conflict = await client.SendAsync(again);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(app.DirectoryPath, "media")));
        Assert.Single(app.Audits());
    }

    /// <summary>后台应有 3D 异常核查入口，模型补录必须通过真实 multipart 接口上传文件。</summary>
    [Fact]
    public async Task Console_HasModelRecoveryWorkflow()
    {
        using var app = new ModelFactory();
        using var client = app.CreateClient();
        var html = await client.GetStringAsync("/admin/");
        var script = await client.GetStringAsync("/admin/orders.js");
        Assert.Contains("data-tab=\"model-review\"", html);
        Assert.Contains("3D 模型生成任务", html);
        Assert.Contains("/api/admin/ai/model-tasks", script);
        Assert.Contains(Tasks, script);
        Assert.Contains("modelFile", script);
        Assert.Contains("form.append('file'", script);
        Assert.Contains("new FormData(", script);
    }

    private static HttpRequestMessage Admin(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "model-admin-key");
        return request;
    }

    private static MultipartFormDataContent Upload(byte[] model)
    {
        var body = new MultipartFormDataContent();
        body.Add(new StringContent(GenerationId), "generation_id");
        body.Add(new StringContent("已核实模型确由服务商产生"), "reason");
        body.Add(new ByteArrayContent(model), "file", "actual.glb");
        return body;
    }

    private static byte[] ValidGlb()
    {
        var bytes = new byte[24];
        Encoding.ASCII.GetBytes("glTF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), 4);
        Encoding.ASCII.GetBytes("JSON").CopyTo(bytes, 16);
        Encoding.ASCII.GetBytes("{}  ").CopyTo(bytes, 20);
        return bytes;
    }

    private sealed class ModelFactory : WebApplicationFactory<Program>
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "tongyin-model-review-" + Guid.NewGuid().ToString("N"));
        private string DbFile => Path.Combine(DirectoryPath, "data.db");

        public ModelFactory()
        {
            Directory.CreateDirectory(Path.Combine(DirectoryPath, "media"));
            using var connection = Open();
            connection.Execute("CREATE TABLE ty_store_lock(id INTEGER PRIMARY KEY); INSERT INTO ty_store_lock(id) VALUES(1);" +
                "CREATE TABLE ty_model_jobs(generation_id TEXT PRIMARY KEY,status TEXT NOT NULL,updated_at TEXT NOT NULL);" +
                "CREATE TABLE ty_works(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);" +
                "CREATE TABLE ty_audit(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL)");
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DbFile};Pooling=False");
        private DbConnection Open() { var result = Connect(); result.Open(); return result; }

        public void Seed(string id, string owner = "openid-a", DateTimeOffset? startedAt = default, string stage = "model", bool claimed = true)
        {
            // Null explicitly means the model queue has not been claimed yet.
            var started = startedAt ?? DateTimeOffset.UtcNow.AddMinutes(-35);
            var payload = JsonSerializer.Serialize(new
            {
                id, owner, created_at = "2026-10-15T10:00:00+08:00",
                status = "model_pending", selected = 0,
                candidates = new[] { "cartoon.png" }, original = "drawing.png",
                model = "", preview = Array.Empty<string>(), image_attempts = 1,
                generation_id = GenerationId, ai_task_stage = claimed ? stage : "",
                ai_started_at = claimed ? started.ToString("O") : "",
                ai_execution_claim_id = claimed ? GenerationId : "", ai_provider_id = "worker-model",
                note = "", custom_field = "保留"
            });
            using var db = Open();
            db.Execute("INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES (@Id,@Owner,@CreatedAt,@Payload)",
                new { Id = id, Owner = owner, CreatedAt = "2026-10-15T10:00:00+08:00", Payload = payload });
        }

        public string Work(string id)
        {
            using var db = Open();
            return db.QuerySingle<string>("SELECT payload FROM ty_works WHERE id=@Id", new { Id = id });
        }

        public string[] Audits()
        {
            using var db = Open();
            return db.Query<string>("SELECT payload FROM ty_audit").ToArray();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, settings) => settings.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "model-recovery-session-secret-of-good-length", ["DB_DRIVER"] = "postgres",
                ["DB_CONNECTION_STRING"] = "unused", ["PUBLIC_URL"] = "http://localhost",
                ["DATA_DIR"] = DirectoryPath,
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60", ["AI_MODEL_DISPATCH_POLL_SECONDS"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                Tongyin.Api.Tests.Auth.TestAdminAuthExtensions.AddTestAdminAuthentication(services, "model-admin-key");
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }
}
