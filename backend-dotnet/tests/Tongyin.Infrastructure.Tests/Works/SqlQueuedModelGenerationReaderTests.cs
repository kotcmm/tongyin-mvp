using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Tongyin.Infrastructure.Works;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Works;

/// <summary>验证模型任务发现和持久化认领在真实事务下不会重复收费。</summary>
public sealed class SqlQueuedModelGenerationReaderTests
{
    /// <summary>只派发当前 .NET 创建且未认领的模型任务；认领后不再被队列发现。</summary>
    [Fact]
    public async Task SelectAndClaim_OnlyOneSubmissionEligible()
    {
        using var database = new ModelTaskDatabase();
        database.Seed("model_pending", marker: true);
        var reader = new SqlQueuedModelGenerationReader(database.Connect);

        var next = Assert.Single(await reader.ListAsync(1));
        Assert.Equal("work-1", next.WorkId);
        Assert.Equal("job-1", next.GenerationId);

        var repo = new SqlWorkRepository(database.Connect);
        var claimed = await repo.ClaimAsync(next.WorkId, next.Owner, next.GenerationId,
            "provider-1", 7, DateTimeOffset.UtcNow);

        Assert.NotNull(claimed);
        Assert.Equal("job-1", claimed.AiExecutionClaimId);
        Assert.Equal("model", claimed.AiTaskStage);
        Assert.Equal("provider-1", claimed.AiProviderId);
        Assert.Equal(7, claimed.AiPromptVersion);
        Assert.Empty(await reader.ListAsync(1));
        Assert.Null(await repo.ClaimAsync(next.WorkId, next.Owner, next.GenerationId,
            "provider-1", 7, DateTimeOffset.UtcNow));
    }

    /// <summary>没有旧版人工标记也可以认领合法模型任务，新的任务表负责幂等性。</summary>
    [Fact]
    public async Task PendingWorkWithoutCompatibilityMarker_IsDispatchedOnce()
    {
        using var database = new ModelTaskDatabase();
        database.Seed("model_pending", marker: false);
        var next = Assert.Single(await new SqlQueuedModelGenerationReader(database.Connect).ListAsync(1));
        var result = await new SqlWorkRepository(database.Connect).ClaimAsync(
            next.WorkId, next.Owner, next.GenerationId, "provider-1", 1, DateTimeOffset.UtcNow);
        Assert.NotNull(result);
        Assert.Empty(await new SqlQueuedModelGenerationReader(database.Connect).ListAsync(1));
    }

    /// <summary>图片尚未确认、错误任务编号、其他微信用户均无资格认领。</summary>
    [Theory]
    [InlineData("model_pending", "old-job", "owner-1")]
    [InlineData("model_pending", "job-1", "owner-other")]
    [InlineData("image_ready", "job-1", "owner-1")]
    public async Task InvalidTask_IsNotClaimed(string status, string generationId, string owner)
    {
        using var database = new ModelTaskDatabase();
        database.Seed(status, marker: true);
        var before = database.Read();

        var claimed = await new SqlWorkRepository(database.Connect).ClaimAsync(
            "work-1", owner, generationId, "provider-1", 1, DateTimeOffset.UtcNow);

        Assert.Null(claimed);
        Assert.Equal(before, database.Read());
    }

    private sealed class ModelTaskDatabase : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "tongyin-model-queue-" + Guid.NewGuid().ToString("N") + ".db");

        public ModelTaskDatabase()
        {
            using var db = Connect();
            db.Open();
            db.Execute("CREATE TABLE ty_works(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL); CREATE TABLE ty_store_lock(id INTEGER PRIMARY KEY); INSERT INTO ty_store_lock(id) VALUES (1); CREATE TABLE ty_model_jobs(generation_id TEXT PRIMARY KEY, work_id TEXT,owner_id TEXT,provider_id TEXT,prompt_version INTEGER,provider_job_id TEXT,status TEXT,created_at TEXT,updated_at TEXT)");
        }

        public DbConnection Connect() => new SqliteConnection($"Data Source={_file};Pooling=False");

        public void Seed(string status, bool marker)
        {
            var payload = JsonSerializer.Serialize(new
            {
                id = "work-1", owner = "owner-1", created_at = "2026-10-17T08:00:00+08:00",
                original = "drawing.png", candidates = new[] { "cartoon.png" },
                selected = 0, preview = Array.Empty<string>(), model = "",
                status, generation_id = "job-1", image_attempts = 1, note = "",
                ai_task_stage = "", ai_started_at = ""
            });
            using var json = JsonDocument.Parse(payload);
            var map = System.Text.Json.Nodes.JsonNode.Parse(json.RootElement.GetRawText())!.AsObject();
            if (marker) map["ai_execution_claim_id"] = "";
            using var db = Connect();
            db.Open();
            db.Execute("INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES ('work-1','owner-1','2026-10-17',@Payload)",
                new { Payload = map.ToJsonString() });
        }

        public string Read()
        {
            using var db = Connect();
            db.Open();
            return db.QuerySingle<string>("SELECT payload FROM ty_works WHERE id='work-1'");
        }

        public void Dispose()
        {
            if (File.Exists(_file)) File.Delete(_file);
        }
    }
}
