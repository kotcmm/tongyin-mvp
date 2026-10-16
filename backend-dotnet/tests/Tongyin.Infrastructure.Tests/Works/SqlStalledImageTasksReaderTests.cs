using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Tongyin.Infrastructure.Works;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Works;

/// <summary>
/// 核查已认领但长时间未完成的图片生成任务，兼容 Go 和 .NET 的 JSON 记录。
/// </summary>
public sealed class SqlStalledImageTasksReaderTests
{
    private static readonly DateTimeOffset Cutoff = DateTimeOffset.Parse("2026-10-16T10:00:00+08:00", CultureInfo.InvariantCulture);

    /// <summary>
    /// 仅返回已认领、超时、仍处于图片待生成状态的记录。
    /// </summary>
    [Fact]
    public async Task ListAsync_StalledImageOnly_FiltersOtherStatesAndUnclaimed()
    {
        using var db = new TestDatabase();
        await db.InsertAsync("stalled", "image_pending", "job-1", "job-1", "image", "2026-10-16T09:00:00+08:00");
        await db.InsertAsync("ready", "image_ready", "job-2", "job-2", "image", "2026-10-16T08:00:00+08:00");
        await db.InsertAsync("failed", "failed", "job-3", "job-3", "image", "2026-10-16T08:00:00+08:00");
        await db.InsertAsync("model", "model_pending", "job-4", "job-4", "model", "2026-10-16T08:00:00+08:00");
        await db.InsertAsync("unclaimed", "image_pending", "job-5", "", "", "");
        await db.InsertAsync("recent", "image_pending", "job-6", "job-6", "image", "2026-10-16T09:59:59Z");

        var results = await new SqlStalledImageTasksReader(db.CreateConnection).ListAsync(Cutoff, 100);

        var task = Assert.Single(results);
        Assert.Equal("stalled", task.WorkId);
        Assert.Equal("job-1", task.GenerationId);
        Assert.Equal("2026-10-16T09:00:00+08:00", task.StartedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
        Assert.False(task.IsLegacyGo);
    }

    /// <summary>
    /// Go 旧任务只有图片阶段开始时间，没有 .NET 认领编号，也应进入人工核查清单。
    /// </summary>
    [Fact]
    public async Task ListAsync_GoImageStartedWithoutClaim_IncludedAsLegacy()
    {
        using var db = new TestDatabase();
        await db.InsertAsync("legacy", "image_pending", "go-job", "", "image", "2026-10-16T08:00:00+08:00");

        var result = Assert.Single(await new SqlStalledImageTasksReader(db.CreateConnection).ListAsync(Cutoff, 20));

        Assert.Equal("legacy", result.WorkId);
        Assert.Equal("go-job", result.GenerationId);
        Assert.True(result.IsLegacyGo);
    }

    /// <summary>
    /// 时间比较必须保留时区含义，恰好超过核查阈值的任务可以出现。
    /// </summary>
    [Fact]
    public async Task ListAsync_ExactCutoffWithDifferentOffset_Included()
    {
        using var db = new TestDatabase();
        await db.InsertAsync("boundary", "image_pending", "job-b", "job-b", "image", "2026-10-16T02:00:00Z");

        var result = Assert.Single(await new SqlStalledImageTasksReader(db.CreateConnection).ListAsync(Cutoff, 10));

        Assert.Equal("boundary", result.WorkId);
    }

    /// <summary>
    /// 按开始时间最早优先排序并限制返回数量，避免一次将全部作品发给管理端。
    /// </summary>
    [Fact]
    public async Task ListAsync_OrdersOldestFirstAndLimitsResults()
    {
        using var db = new TestDatabase();
        await db.InsertAsync("later", "image_pending", "job-b", "job-b", "image", "2026-10-16T09:00:00+08:00");
        await db.InsertAsync("earlier", "image_pending", "job-a", "job-a", "image", "2026-10-16T07:00:00+08:00");
        await db.InsertAsync("middle", "image_pending", "job-c", "job-c", "image", "2026-10-16T08:00:00+08:00");

        var results = await new SqlStalledImageTasksReader(db.CreateConnection).ListAsync(Cutoff, 2);

        Assert.Equal(new[] { "earlier", "middle" }, results.Select(x => x.WorkId).ToArray());
    }

    /// <summary>
    /// 空或格式错误的时间、错误认领编号不能错误判断为已认领并自动干预。
    /// </summary>
    [Fact]
    public async Task ListAsync_InvalidMarkers_NotReported()
    {
        using var db = new TestDatabase();
        await db.InsertAsync("badtime", "image_pending", "job-a", "job-a", "image", "invalid-time");
        await db.InsertAsync("other-claim", "image_pending", "job-b", "job-old", "image", "2026-10-16T07:00:00+08:00");
        await db.InsertAsync("no-id", "image_pending", "", "", "image", "2026-10-16T07:00:00+08:00");

        Assert.Empty(await new SqlStalledImageTasksReader(db.CreateConnection).ListAsync(Cutoff, 20));
    }

    /// <summary>
    /// 核查清单是纯只读查询，不改变作品状态、生成任务或认领标记。
    /// </summary>
    [Fact]
    public async Task ListAsync_IsReadOnlyAndDoesNotExposeOwner()
    {
        using var db = new TestDatabase();
        await db.InsertAsync("work-1", "image_pending", "job-1", "job-1", "image", "2026-10-16T08:00:00+08:00");
        var before = await db.ReadPayloadAsync("work-1");

        var result = Assert.Single(await new SqlStalledImageTasksReader(db.CreateConnection).ListAsync(Cutoff, 100));

        Assert.Equal(before, await db.ReadPayloadAsync("work-1"));
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("openid-001", json);
        Assert.DoesNotContain("api_key", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 核查上限不能被任意扩大。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task ListAsync_InvalidLimit_Rejects(int limit)
    {
        using var db = new TestDatabase();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new SqlStalledImageTasksReader(db.CreateConnection).ListAsync(Cutoff, limit));
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), $"tongyin-stalled-{Guid.NewGuid():N}.db");

        public TestDatabase()
        {
            using var connection = CreateConnection();
            connection.Open();
            connection.Execute("CREATE TABLE ty_works (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL)");
        }

        public DbConnection CreateConnection() => new SqliteConnection($"Data Source={_file};Pooling=False");

        public async Task InsertAsync(string id, string status, string generationId, string claimId, string stage, string startedAt)
        {
            var payload = JsonSerializer.Serialize(new
            {
                id, owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
                original = "drawing.png", status, generation_id = generationId,
                ai_execution_claim_id = claimId, ai_task_stage = stage, ai_started_at = startedAt,
                ai_provider_id = "seedream-01", image_attempts = 1, api_key_cipher = "never-return"
            });
            await using var connection = CreateConnection();
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO ty_works(id, owner_id, created_at, payload) VALUES (@Id, @Owner, @CreatedAt, @Payload)",
                new { Id = id, Owner = "openid-001", CreatedAt = "2026-10-15T10:00:00+08:00", Payload = payload });
        }

        public async Task<string> ReadPayloadAsync(string id)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();
            return await connection.QuerySingleAsync<string>("SELECT payload FROM ty_works WHERE id = @Id", new { Id = id });
        }

        public void Dispose()
        {
            if (File.Exists(_file))
                File.Delete(_file);
        }
    }
}
