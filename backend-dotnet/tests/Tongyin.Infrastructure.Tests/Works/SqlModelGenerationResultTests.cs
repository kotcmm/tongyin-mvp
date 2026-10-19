using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Tongyin.Infrastructure.Works;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Works;

/// <summary>通过 SQLite 的实际事务验证模型生成结果原子提交与旧 Go 作品兼容性。</summary>
public sealed class SqlModelGenerationResultTests
{
    /// <summary>正确回调生成的 GLB 写入当前作品，不丢弃已确认卡通图与历史字段。</summary>
    [Fact]
    public async Task Complete_CurrentGeneration_PreservesSelectedImageAndLegacyFields()
    {
        using var db = new ModelDatabase();
        db.Seed("model_pending", "job-1");
        var repo = new SqlWorkRepository(db.CreateConnection);

        var result = await repo.CompleteModelGenerationAsync("work-1", "job-1", "saved.glb");

        Assert.Equal("model_ready", result.Status);
        Assert.Equal("saved.glb", result.Model);
        using var json = JsonDocument.Parse(db.Read());
        Assert.Equal("legacy-value", json.RootElement.GetProperty("legacy_field").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("selected").GetInt32());
        Assert.Equal("cartoon.png", Assert.Single(json.RootElement.GetProperty("candidates").EnumerateArray()).GetString());
    }

    /// <summary>旧任务和非模型处理中状态必须拒绝，数据库任何字段不得变化。</summary>
    [Theory]
    [InlineData("model_pending", "old-job")]
    [InlineData("model_ready", "job-1")]
    [InlineData("image_ready", "job-1")]
    public async Task Complete_InvalidGenerationOrState_LeavesDatabaseUntouched(string status, string generationId)
    {
        using var db = new ModelDatabase();
        db.Seed(status, "job-1");
        var before = db.Read();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlWorkRepository(db.CreateConnection).CompleteModelGenerationAsync("work-1", generationId, "old.glb"));

        Assert.Equal(before, db.Read());
    }

    /// <summary>模型明确失败只保存失败原因，不能删除用户已确认的形象图。</summary>
    [Fact]
    public async Task Fail_CurrentGeneration_PreservesCandidate()
    {
        using var db = new ModelDatabase();
        db.Seed("model_pending", "job-1");

        var result = await new SqlWorkRepository(db.CreateConnection).FailModelGenerationAsync("work-1", "job-1", "服务商返回错误");

        Assert.Equal("failed", result.Status);
        Assert.Equal("服务商返回错误", result.Note);
        Assert.Equal("cartoon.png", Assert.Single(result.Candidates));
        Assert.Equal(string.Empty, result.Model);
    }

    private sealed class ModelDatabase : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "tongyin-model-result-" + Guid.NewGuid().ToString("N") + ".db");

        public ModelDatabase()
        {
            using var db = CreateConnection();
            db.Open();
            db.Execute("CREATE TABLE ty_works (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL); CREATE TABLE ty_store_lock (id INTEGER PRIMARY KEY); INSERT INTO ty_store_lock(id) VALUES (1); CREATE TABLE ty_model_jobs(generation_id TEXT PRIMARY KEY,status TEXT NOT NULL,updated_at TEXT NOT NULL)");
        }

        public DbConnection CreateConnection() => new SqliteConnection($"Data Source={_file};Pooling=False");

        public void Seed(string status, string generationId)
        {
            var payload = JsonSerializer.Serialize(new
            {
                id = "work-1", owner = "owner-1", created_at = "2026-10-16T09:00:00+08:00",
                original = "original.png", candidates = new[] { "cartoon.png" },
                selected = 0, preview = Array.Empty<string>(), model = "", status,
                generation_id = generationId, image_attempts = 1, note = "",
                legacy_field = "legacy-value"
            });
            using var db = CreateConnection();
            db.Open();
            db.Execute("INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES ('work-1','owner-1','2026-10-16',@Payload)", new { Payload = payload });
        }

        public string Read()
        {
            using var db = CreateConnection();
            db.Open();
            return db.QuerySingle<string>("SELECT payload FROM ty_works WHERE id='work-1'");
        }

        public void Dispose()
        {
            if (File.Exists(_file)) File.Delete(_file);
        }
    }
}
