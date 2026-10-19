using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Tongyin.Infrastructure.Works;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Works;

/// <summary>
/// 管理员人工核查必须在 Go 兼容的事务内同步更新作品与审计日志。
/// </summary>
public sealed class SqlAdminImageReviewRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(8));

    /// <summary>
    /// 核实失败后写入失败状态与 Go 格式审计记录，审计包含编号和处理依据。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ConfirmedFailure_WritesWorkAndAuditTogether()
    {
        using var db = new TestDatabase();
        await db.SeedAsync("work-1", "image_pending", "job-1", "job-1", Now.AddHours(-1));
        var repository = new SqlAdminImageReviewRepository(db.Connect);

        var result = await repository.ResolveAsync("work-1", "job-1", "legacy-token", "模型控制台确认未生成", null, Now);

        Assert.Equal("failed", result.Status);
        Assert.Equal("AI 生成失败，请联系客服", result.Note);
        Assert.Equal("job-1", result.GenerationId);
        Assert.Equal(1, result.ImageAttempts);
        using var audit = JsonDocument.Parse(Assert.Single(await db.AuditsAsync()));
        var row = audit.RootElement;
        Assert.Equal("legacy-token", row.GetProperty("owner").GetString());
        Assert.Equal("ai.image.review.failed", row.GetProperty("action").GetString());
        Assert.Equal("work-1", row.GetProperty("target").GetString());
        using var detail = JsonDocument.Parse(row.GetProperty("detail").GetString()!);
        Assert.Equal("job-1", detail.RootElement.GetProperty("generation_id").GetString());
        Assert.Equal("模型控制台确认未生成", detail.RootElement.GetProperty("reason").GetString());
    }

    /// <summary>
    /// 管理员补录已有文件后只能产生一张候选图片，并保留原始 JSON 额外字段。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ConfirmedImage_PersistsSingleResultAndAudit()
    {
        using var db = new TestDatabase();
        await db.SeedAsync("work-1", "image_pending", "job-1", "job-1", Now.AddHours(-1));
        var repo = new SqlAdminImageReviewRepository(db.Connect);

        var result = await repo.ResolveAsync("work-1", "job-1", "legacy-token", "已在服务商后台核对图片", "recovered.png", Now);

        Assert.Equal("image_ready", result.Status);
        Assert.Equal("recovered.png", Assert.Single(result.Candidates));
        using var work = JsonDocument.Parse(await db.WorkAsync("work-1"));
        Assert.Equal("保留", work.RootElement.GetProperty("custom_field").GetString());
        Assert.Equal("job-1", work.RootElement.GetProperty("generation_id").GetString());
        using var audit = JsonDocument.Parse(Assert.Single(await db.AuditsAsync()));
        Assert.Equal("ai.image.review.completed", audit.RootElement.GetProperty("action").GetString());
        using var detail = JsonDocument.Parse(audit.RootElement.GetProperty("detail").GetString()!);
        Assert.Equal("recovered.png", detail.RootElement.GetProperty("file_name").GetString());
    }

    /// <summary>
    /// 仅在已认领且开始超过 15 分钟的当前图片任务上允许人工处理。
    /// </summary>
    [Theory]
    [InlineData("job-old", "job-1", "image_pending", "image", 60)]
    [InlineData("job-1", "job-1", "image_ready", "image", 60)]
    [InlineData("job-1", "job-1", "failed", "image", 60)]
    [InlineData("job-1", "job-1", "image_pending", "model", 60)]
    [InlineData("job-1", "job-1", "image_pending", "image", 5)]
    public async Task ResolveAsync_WrongTaskOrState_RejectsWithoutAudit(string submittedId, string storedId, string status, string stage, int ageMinutes)
    {
        using var db = new TestDatabase();
        await db.SeedAsync("work-1", status, storedId, storedId, Now.AddMinutes(-ageMinutes), stage);
        var before = await db.WorkAsync("work-1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlAdminImageReviewRepository(db.Connect).ResolveAsync(
                "work-1", submittedId, "legacy-token", "确认", null, Now));

        Assert.Equal(before, await db.WorkAsync("work-1"));
        Assert.Empty(await db.AuditsAsync());
    }

    /// <summary>没有正式认领编号的任务不能人工核查或更改作品状态。</summary>
    [Fact]
    public async Task ResolveAsync_MissingClaimId_Rejects()
    {
        using var db = new TestDatabase();
        await db.SeedAsync("work-1", "image_pending", "job-1", "", Now.AddHours(-2));
        var original = await db.WorkAsync("work-1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlAdminImageReviewRepository(db.Connect)
                .ResolveAsync("work-1", "job-1", "admin", "已确认失败", null, Now));

        Assert.Equal(original, await db.WorkAsync("work-1"));
        Assert.Empty(await db.AuditsAsync());
    }

    /// <summary>
    /// 认领编号与当前生成编号不一致时，不允许审计或覆盖作品。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_StaleClaim_Rejects()
    {
        using var db = new TestDatabase();
        await db.SeedAsync("work-1", "image_pending", "job-new", "job-old", Now.AddHours(-1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlAdminImageReviewRepository(db.Connect).ResolveAsync(
                "work-1", "job-new", "legacy-token", "确认", "image.png", Now));

        Assert.Empty(await db.AuditsAsync());
    }

    /// <summary>
    /// 缺少 Go 审计表时，作品状态修改必须整体回滚。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AuditInsertFails_RollsBackWork()
    {
        using var db = new TestDatabase();
        await db.SeedAsync("work-1", "image_pending", "job-1", "job-1", Now.AddHours(-1));
        var before = await db.WorkAsync("work-1");
        db.DropAudit();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new SqlAdminImageReviewRepository(db.Connect).ResolveAsync(
                "work-1", "job-1", "legacy-token", "已确认失败", null, Now));

        Assert.Equal(before, await db.WorkAsync("work-1"));
    }

    /// <summary>
    /// 同一任务只能被人工处理一次，重复处理不得产生第二条审计。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_SecondReview_Rejects()
    {
        using var db = new TestDatabase();
        await db.SeedAsync("work-1", "image_pending", "job-1", "job-1", Now.AddHours(-1));
        var repository = new SqlAdminImageReviewRepository(db.Connect);
        await repository.ResolveAsync("work-1", "job-1", "legacy-token", "已核实", null, Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.ResolveAsync("work-1", "job-1", "legacy-token", "重复", "late.png", Now));

        Assert.Single(await db.AuditsAsync());
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "tongyin-review-" + Guid.NewGuid().ToString("N") + ".db");

        public TestDatabase()
        {
            using var db = Connect();
            db.Open();
            db.Execute("CREATE TABLE ty_store_lock (id INTEGER PRIMARY KEY); INSERT INTO ty_store_lock(id) VALUES (1);" +
                "CREATE TABLE ty_works (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);" +
                "CREATE TABLE ty_audit (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL)");
        }

        public DbConnection Connect() => new SqliteConnection($"Data Source={_file};Pooling=False");

        public async Task SeedAsync(string id, string status, string generationId, string claimId, DateTimeOffset started, string stage = "image")
        {
            var payload = JsonSerializer.Serialize(new
            {
                id, owner = "openid-001", original = "drawing.png", status,
                created_at = "2026-10-15T10:00:00+08:00",
                generation_id = generationId, ai_execution_claim_id = claimId,
                ai_started_at = started.ToString("yyyy-MM-dd'T'HH:mm:sszzz"), ai_task_stage = stage,
                image_attempts = 1, selected = -1, candidates = Array.Empty<string>(),
                note = "", model = "", custom_field = "保留"
            });
            await using var db = Connect();
            await db.OpenAsync();
            await db.ExecuteAsync("INSERT INTO ty_works(id, owner_id, created_at, payload) VALUES (@Id, @Owner, @Created, @Payload)",
                new { Id = id, Owner = "openid-001", Created = "2026-10-15T10:00:00+08:00", Payload = payload });
        }

        public async Task<string> WorkAsync(string id)
        {
            await using var db = Connect();
            await db.OpenAsync();
            return await db.QuerySingleAsync<string>("SELECT payload FROM ty_works WHERE id=@Id", new { Id = id });
        }

        public async Task<string[]> AuditsAsync()
        {
            await using var db = Connect();
            await db.OpenAsync();
            return (await db.QueryAsync<string>("SELECT payload FROM ty_audit")).ToArray();
        }

        public void DropAudit()
        {
            using var db = Connect();
            db.Open();
            db.Execute("DROP TABLE ty_audit");
        }

        public void Dispose()
        {
            if (File.Exists(_file)) File.Delete(_file);
        }
    }
}
