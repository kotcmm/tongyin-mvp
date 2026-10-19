using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Tongyin.Infrastructure.Media;
using Tongyin.Infrastructure.Works;
using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Media;

/// <summary>作品删除后的异步媒体回收测试：避免误删订单、共享文件及危险路径。</summary>
public sealed class PendingMediaCleanupTests
{
    /// <summary>数据库删除后，回收器能删除无引用的原画和 GLB，并确认任务完成。</summary>
    [Fact]
    public async Task ProcessNext_DeletedWorkMedia_RemovesOnlyUnreferencedFile()
    {
        using var f = new Fixture();
        var image = "abcdef0123456789abcdef01.png";
        var model = "abcdef0123456789abcdef02.glb";
        f.CreateMedia(image);
        f.CreateMedia(model);
        f.SeedWork("w1", "user1", image, model);

        Assert.Equal(DeleteWorkResult.Deleted, await f.Works.DeleteWorkAsync("w1", "user1"));
        Assert.Equal(2, f.PendingCount());
        Assert.True(File.Exists(f.PathFor(image)));
        Assert.True(await f.Cleaner.ProcessNextAsync());
        Assert.True(await f.Cleaner.ProcessNextAsync());
        Assert.False(File.Exists(f.PathFor(image)));
        Assert.False(File.Exists(f.PathFor(model)));
        Assert.Equal(0, f.PendingCount());
    }

    /// <summary>一个文件仍被另一个作品引用时不得删除；引用解除后再次入队才允许清理。</summary>
    [Fact]
    public async Task ProcessNext_SharedImage_IsKeptUntilLastWorkDeleted()
    {
        using var f = new Fixture();
        var image = "abcdef0123456789abcdef01.png";
        f.CreateMedia(image);
        f.SeedWork("w1", "user1", image);
        f.SeedWork("w2", "user2", image);

        Assert.Equal(DeleteWorkResult.Deleted, await f.Works.DeleteWorkAsync("w1", "user1"));
        Assert.True(await f.Cleaner.ProcessNextAsync());
        Assert.True(File.Exists(f.PathFor(image)), "其他作品还使用该文件");
        Assert.Equal(0, f.PendingCount());

        Assert.Equal(DeleteWorkResult.Deleted, await f.Works.DeleteWorkAsync("w2", "user2"));
        Assert.True(await f.Cleaner.ProcessNextAsync());
        Assert.False(File.Exists(f.PathFor(image)));
        Assert.Equal(0, f.PendingCount());
    }

    /// <summary>文件被订单快照直接引用时保留，不误删履约材料。</summary>
    [Fact]
    public async Task ProcessNext_OrderDirectlyReferencesImage_DoesNotDelete()
    {
        using var f = new Fixture();
        var image = "abcdef0123456789abcdef01.png";
        f.CreateMedia(image);
        f.SeedWork("w1", "user1", image);
        f.SeedOrderWithMedia("order2", image);

        Assert.Equal(DeleteWorkResult.Deleted, await f.Works.DeleteWorkAsync("w1", "user1"));
        Assert.True(await f.Cleaner.ProcessNextAsync());
        Assert.True(File.Exists(f.PathFor(image)));
        Assert.Equal(0, f.PendingCount());
    }

    /// <summary>删除磁盘文件失败时保留数据库回收记录，供后续重试而不会遗忘文件。</summary>
    [Fact]
    public async Task ProcessNext_StorageFailure_KeepsQueueForRetry()
    {
        using var f = new Fixture();
        var image = "abcdef0123456789abcdef01.png";
        f.CreateMedia(image);
        f.SeedWork("w1", "user1", image);
        Assert.Equal(DeleteWorkResult.Deleted, await f.Works.DeleteWorkAsync("w1", "user1"));

        // 模拟磁盘对象无法被删除：同名目录会让 File.Delete 抛出 IOException。
        File.Delete(f.PathFor(image));
        Directory.CreateDirectory(f.PathFor(image));
        await Assert.ThrowsAnyAsync<IOException>(() => f.Cleaner.ProcessNextAsync());
        Assert.Equal(1, f.PendingCount());
        Directory.Delete(f.PathFor(image));
        Assert.True(await f.Cleaner.ProcessNextAsync());
        Assert.Equal(0, f.PendingCount());
    }

    /// <summary>清理器重启后能继续处理上次提交到数据库的任务，无需保留内存状态。</summary>
    [Fact]
    public async Task ProcessNext_NewServiceInstance_ResumesPersistedQueue()
    {
        using var f = new Fixture();
        var image = "abcdef0123456789abcdef01.png";
        f.CreateMedia(image);
        f.SeedWork("w1", "user1", image);
        Assert.Equal(DeleteWorkResult.Deleted, await f.Works.DeleteWorkAsync("w1", "user1"));
        var newCleaner = new PendingMediaCleanup(f.CreateConnection, f.Media);

        Assert.True(await newCleaner.ProcessNextAsync());
        Assert.False(File.Exists(f.PathFor(image)));
        Assert.Equal(0, f.PendingCount());
    }

    /// <summary>不在队列中的已有文件不能由清理器触碰。</summary>
    [Fact]
    public async Task ProcessNext_NoDeletionQueued_DoesNotTouchLocalMedia()
    {
        using var f = new Fixture();
        var image = "abcdef0123456789abcdef01.png";
        f.CreateMedia(image);

        Assert.False(await f.Cleaner.ProcessNextAsync());
        Assert.True(File.Exists(f.PathFor(image)));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-media-cleanup-" + Guid.NewGuid().ToString("N"));
        private string DbPath => Path.Combine(_root, "cleanup.sqlite");
        private string MediaPath => Path.Combine(_root, "media");
        public LocalMediaStore Media { get; }
        public SqlWorkRepository Works { get; }
        public PendingMediaCleanup Cleaner { get; }

        public Fixture()
        {
            Directory.CreateDirectory(MediaPath);
            using var db = CreateConnection();
            db.Open();
            db.Execute("""
                CREATE TABLE ty_store_lock(id INTEGER PRIMARY KEY);
                INSERT INTO ty_store_lock(id) VALUES(1);
                CREATE TABLE ty_works(id TEXT PRIMARY KEY,owner_id TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL);
                CREATE TABLE ty_orders(id TEXT PRIMARY KEY,owner_id TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL);
                CREATE TABLE ty_media_cleanup(file_name TEXT PRIMARY KEY,requested_at TEXT NOT NULL);
                """);
            Media = new LocalMediaStore(_root, "http://localhost:18080", "test-media-key-more-than-24-characters");
            Works = new SqlWorkRepository(CreateConnection);
            Cleaner = new PendingMediaCleanup(CreateConnection, Media);
        }

        public DbConnection CreateConnection() => new SqliteConnection($"Data Source={DbPath};Pooling=False");

        public int PendingCount()
        {
            using var db = CreateConnection();
            db.Open();
            return db.ExecuteScalar<int>("SELECT COUNT(*) FROM ty_media_cleanup");
        }

        public string PathFor(string name) => Path.Combine(MediaPath, name);

        public void CreateMedia(string name) => File.WriteAllBytes(PathFor(name), [1, 2, 3]);

        public void SeedWork(string id, string owner, string original, string model = "")
        {
            using var db = CreateConnection();
            db.Open();
            db.Execute("INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES(@Id,@Owner,@Time,@Payload)",
                new { Id = id, Owner = owner, Time = "2026-10-19", Payload = JsonSerializer.Serialize(new {
                    id, status = "model_ready", original, model
                }) });
        }

        public void SeedOrderWithMedia(string id, string file)
        {
            using var db = CreateConnection();
            db.Open();
            db.Execute("INSERT INTO ty_orders(id,owner_id,created_at,payload) VALUES(@Id,@Owner,@Time,@Payload)",
                new { Id = id, Owner = "user2", Time = "2026-10-19",
                    Payload = JsonSerializer.Serialize(new { id, work_id = "other-work", snapshot = new { image = file } }) });
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
