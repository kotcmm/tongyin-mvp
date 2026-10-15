using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Tongyin.Infrastructure.Works;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Works;

/// <summary>
/// 验证作品持久化适配器使用现有 Go 后端的 ty_works 结构。
/// </summary>
public sealed class SqlWorkRepositoryTests
{
    /// <summary>
    /// 创建用例必须真正写入 ty_works，且作品 JSON 可被旧版 Go 后端识别。
    /// </summary>
    [Fact]
    public async Task CreateWork_StoresGoCompatibleWorkDocument()
    {
        using var db = new TestWorkDatabase(createTable: true);
        var useCase = new CreateWork(new SqlWorkRepository(db.CreateConnection));
        var createdAt = new DateTimeOffset(2026, 10, 15, 10, 30, 0, TimeSpan.FromHours(8));

        await useCase.ExecuteAsync("work-001", "openid-001", createdAt, "drawing.png");

        using var connection = db.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT owner_id, created_at, payload FROM ty_works WHERE id='work-001'";
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("openid-001", reader.GetString(0));
        Assert.Equal("2026-10-15T10:30:00+08:00", reader.GetString(1));

        using var json = JsonDocument.Parse(reader.GetString(2));
        var value = json.RootElement;
        Assert.Equal("work-001", value.GetProperty("id").GetString());
        Assert.Equal("openid-001", value.GetProperty("owner").GetString());
        Assert.Equal("2026-10-15T10:30:00+08:00", value.GetProperty("created_at").GetString());
        Assert.Equal("drawing.png", value.GetProperty("original").GetString());
        Assert.Equal("image_pending", value.GetProperty("status").GetString());
        Assert.Equal(-1, value.GetProperty("selected").GetInt32());
        Assert.Equal(0, value.GetProperty("image_attempts").GetInt32());
        Assert.Empty(value.GetProperty("candidates").EnumerateArray());
        Assert.Empty(value.GetProperty("preview").EnumerateArray());
        Assert.False(await reader.ReadAsync());
    }

    /// <summary>
    /// 包含引号、分号和非 ASCII 字符的原画路径必须按数据保存，不得解释为 SQL。
    /// </summary>
    [Fact]
    public async Task CreateWork_WithSpecialCharacters_PreservesValuesAndTable()
    {
        using var db = new TestWorkDatabase(createTable: true);
        var useCase = new CreateWork(new SqlWorkRepository(db.CreateConnection));
        const string original = "儿童画'; DROP TABLE ty_works; --.png";

        await useCase.ExecuteAsync("work-001", "openid-001", DateTimeOffset.UtcNow, original);

        using var connection = db.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM ty_works WHERE id='work-001'";
        var payload = (string?)await command.ExecuteScalarAsync();
        Assert.NotNull(payload);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(original, json.RootElement.GetProperty("original").GetString());

        command.CommandText = "SELECT COUNT(*) FROM ty_works";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    /// <summary>
    /// 主键冲突必须使创建用例失败，且不能覆盖已有作品。
    /// </summary>
    [Fact]
    public async Task CreateWork_DuplicateId_ThrowsWithoutOverwriting()
    {
        using var db = new TestWorkDatabase(createTable: true);
        var useCase = new CreateWork(new SqlWorkRepository(db.CreateConnection));
        var createdAt = DateTimeOffset.UtcNow;
        await useCase.ExecuteAsync("work-001", "openid-001", createdAt, "first.png");

        await Assert.ThrowsAnyAsync<DbException>(() => useCase.ExecuteAsync("work-001", "openid-002", createdAt, "second.png"));

        using var connection = db.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT owner_id, payload FROM ty_works WHERE id='work-001'";
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("openid-001", reader.GetString(0));
        using var json = JsonDocument.Parse(reader.GetString(1));
        Assert.Equal("first.png", json.RootElement.GetProperty("original").GetString());
    }

    /// <summary>
    /// 缺少作品数据表时，持久化异常必须向调用方传播。
    /// </summary>
    [Fact]
    public async Task CreateWork_TableMissing_PropagatesDatabaseFailure()
    {
        using var db = new TestWorkDatabase(createTable: false);
        var useCase = new CreateWork(new SqlWorkRepository(db.CreateConnection));

        await Assert.ThrowsAnyAsync<DbException>(() => useCase.ExecuteAsync("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png"));
    }

    private sealed class TestWorkDatabase : IDisposable
    {
        private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"tongyin-work-{Guid.NewGuid():N}.db");

        public TestWorkDatabase(bool createTable)
        {
            if (!createTable) return;

            using var connection = CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE ty_works (id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL)";
            command.ExecuteNonQuery();
        }

        public DbConnection CreateConnection() => new SqliteConnection($"Data Source={_databaseFile};Pooling=False");

        public void Dispose()
        {
            if (File.Exists(_databaseFile)) File.Delete(_databaseFile);
        }
    }
}
