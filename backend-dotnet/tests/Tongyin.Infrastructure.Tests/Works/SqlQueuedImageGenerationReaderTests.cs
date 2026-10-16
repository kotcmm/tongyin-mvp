using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Tongyin.Infrastructure.Works;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Works;

/// <summary>
/// 从 Go 兼容的作品表发现尚未认领的图片生成任务，避免重复调用付费模型。
/// </summary>
public sealed class SqlQueuedImageGenerationReaderTests
{
    /// <summary>
    /// 仅返回已登记、未被任何执行器认领的图片生成任务，且以数据库 owner_id 作为真实归属。
    /// </summary>
    [Fact]
    public async Task ListAsync_PendingUnclaimed_OnlyReturnsRegisteredTasks()
    {
        using var db = new TestDatabase();
        await db.InsertAsync("fresh", "openid-001", "image_pending", 1, "job-001", "", "", "");
        await db.InsertAsync("new-work", "openid-002", "image_pending", 0, "", "", "", "");
        await db.InsertAsync("claimed", "openid-002", "image_pending", 1, "job-002", "job-002", "image", "2026-10-16T10:00:00+08:00");
        await db.InsertAsync("go-running", "openid-003", "image_pending", 1, "job-003", "", "image", "2026-10-16T10:00:00+08:00");
        await db.InsertAsync("completed", "openid-004", "image_ready", 1, "job-004", "", "", "");
        await db.InsertAsync("failed", "openid-004", "failed", 1, "job-005", "", "", "");

        var queued = await new SqlQueuedImageGenerationReader(db.CreateConnection).ListAsync(10);

        var task = Assert.Single(queued);
        Assert.Equal("fresh", task.WorkId);
        Assert.Equal("openid-001", task.Owner);
        Assert.Equal("job-001", task.GenerationId);
    }

    /// <summary>
    /// Go 旧记录没有 .NET 登记时写入的认领字段，禁止轮询器自动接管并额外计费。
    /// </summary>
    [Fact]
    public async Task ListAsync_GoPendingWithoutDotnetRegistrationMarker_IsExcluded()
    {
        using var db = new TestDatabase();
        await using (var connection = db.CreateConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO ty_works(id, owner_id, created_at, payload) VALUES (@Id,@Owner,@Created,@Payload)",
                new {
                    Id = "legacy", Owner = "owner-A", Created = "2026-10-15T10:00:00+08:00",
                    Payload = JsonSerializer.Serialize(new {
                        id = "legacy", status = "image_pending", image_attempts = 1,
                        generation_id = "go-job", original = "drawing.png"
                    })
                });
        }

        Assert.Empty(await new SqlQueuedImageGenerationReader(db.CreateConnection).ListAsync(10));
    }

    /// <summary>
    /// 旧任务只要出现历史执行时间，即使遗失认领字段，也不能再次送往付费模型。
    /// </summary>
    [Theory]
    [InlineData("", "2026-10-16T10:00:00+08:00")]
    [InlineData("model", "")]
    [InlineData("image", "")]
    public async Task ListAsync_ExecutionMarkersPresent_Exclude(string stage, string started)
    {
        using var db = new TestDatabase();
        await db.InsertAsync("uncertain", "openid-001", "image_pending", 1, "job-001", "", stage, started);

        Assert.Empty(await new SqlQueuedImageGenerationReader(db.CreateConnection).ListAsync(10));
    }

    /// <summary>
    /// 先派发最早登记的未执行任务；limit=1 时不能跳过其他有效任务或读错用户。
    /// </summary>
    [Fact]
    public async Task ListAsync_OldestFirstAndLimited()
    {
        using var db = new TestDatabase();
        await db.InsertAsync("older", "owner-A", "image_pending", 1, "job-A", "", "", "", "2026-10-14T12:00:00+08:00");
        await db.InsertAsync("newer", "owner-B", "image_pending", 1, "job-B", "", "", "", "2026-10-15T12:00:00+08:00");

        var first = await new SqlQueuedImageGenerationReader(db.CreateConnection).ListAsync(1);

        Assert.Equal("older", Assert.Single(first).WorkId);
    }

    /// <summary>
    /// 只读的轮询绝不改变任务状态和认领字段，是否领取必须由单独事务决定。
    /// </summary>
    [Fact]
    public async Task ListAsync_DoesNotClaimOrMutateWork()
    {
        using var db = new TestDatabase();
        await db.InsertAsync("work-1", "owner-A", "image_pending", 1, "job-1", "", "", "");
        var before = await db.ReadPayloadAsync("work-1");

        Assert.Single(await new SqlQueuedImageGenerationReader(db.CreateConnection).ListAsync(1));

        Assert.Equal(before, await db.ReadPayloadAsync("work-1"));
    }

    /// <summary>
    /// 单次批量读取应有限额，禁止将整库作品无上限交给执行器。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(26)]
    public async Task ListAsync_InvalidLimit_Rejects(int limit)
    {
        using var db = new TestDatabase();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new SqlQueuedImageGenerationReader(db.CreateConnection).ListAsync(limit));
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), $"tongyin-dispatch-{Guid.NewGuid():N}.db");

        public TestDatabase()
        {
            using var db = CreateConnection();
            db.Open();
            db.Execute("CREATE TABLE ty_works(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL)");
        }

        public DbConnection CreateConnection() => new SqliteConnection($"Data Source={_file};Pooling=False");

        public async Task InsertAsync(string id, string owner, string status, int attempts, string generationId, string claimId, string stage, string started, string created = "2026-10-15T10:00:00+08:00")
        {
            var payload = JsonSerializer.Serialize(new
            {
                id,
                owner = "untrusted-payload-owner",
                created_at = created, original = "drawing.png", status,
                generation_id = generationId, image_attempts = attempts,
                ai_execution_claim_id = claimId, ai_task_stage = stage, ai_started_at = started
            });
            await using var db = CreateConnection();
            await db.OpenAsync();
            await db.ExecuteAsync(
                "INSERT INTO ty_works(id, owner_id, created_at, payload) VALUES (@Id,@Owner,@Created,@Payload)",
                new { Id = id, Owner = owner, Created = created, Payload = payload });
        }

        public async Task<string> ReadPayloadAsync(string id)
        {
            await using var db = CreateConnection();
            await db.OpenAsync();
            return await db.QuerySingleAsync<string>("SELECT payload FROM ty_works WHERE id=@Id", new { Id = id });
        }

        public void Dispose()
        {
            if (File.Exists(_file)) File.Delete(_file);
        }
    }
}
