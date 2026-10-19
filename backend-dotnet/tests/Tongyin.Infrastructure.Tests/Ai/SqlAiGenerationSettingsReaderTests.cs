using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Tongyin.Infrastructure.Ai;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Ai;

/// <summary>
/// 验证 .NET 能直接读取 童印运营后台保存的图片及模型生成配置。
/// </summary>
public sealed class SqlAiGenerationSettingsReaderTests
{
    /// <summary>
    /// 图片生成应选择已启用的默认服务商，并读取管理员保存的提示词和版本。
    /// </summary>
    [Fact]
    public async Task GetAsync_Image_ReturnsDefaultEnabledProviderAndSavedPrompt()
    {
        using var db = new TestDatabase();
        await db.InsertProviderAsync("image-disabled", new
        {
            id = "image-disabled", stage = "image", adapter = "worker", endpoint = "https://disabled.example/api",
            model = "ignore", enabled = false, @default = true, api_key_cipher = "unused"
        });
        await db.InsertProviderAsync("image-other", new
        {
            id = "image-other", stage = "image", adapter = "worker", endpoint = "https://other.example/api",
            model = "ignore", enabled = true, @default = false, api_key_cipher = "unused"
        });
        await db.InsertProviderAsync("model-default", new
        {
            id = "model-default", stage = "model", adapter = "hunyuan3d", endpoint = "https://api.ai3d.cloud.tencent.com/v1/ai3d/submit",
            model = "model-v1", enabled = true, @default = true, api_key_cipher = "other-cipher"
        });
        await db.InsertProviderAsync("image-default", new
        {
            id = "image-default", stage = "image", adapter = "seedream", endpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations",
            model = "seedream-v1", enabled = true, @default = true, api_key_cipher = "encrypted-key"
        });
        await db.InsertPromptAsync("image", new { id = "image", version = 7, template = "保留儿童原画的主要特征，只生成一张卡通图片" });

        var result = await new SqlAiGenerationSettingsReader(db.CreateConnection).GetAsync("image");

        Assert.Equal("image", result.Stage);
        Assert.Equal(7, result.PromptVersion);
        Assert.Equal("保留儿童原画的主要特征，只生成一张卡通图片", result.PromptTemplate);
        Assert.NotNull(result.Provider);
        Assert.Equal("image-default", result.Provider.Id);
        Assert.Equal("seedream", result.Provider.Adapter);
        Assert.Equal("seedream-v1", result.Provider.Model);
        Assert.Equal("https://ark.cn-beijing.volces.com/api/v3/images/generations", result.Provider.Endpoint);
        Assert.Equal("encrypted-key", result.Provider.ApiKeyCipher);
    }

    /// <summary>
    /// 没有启用且默认的图片服务商时，不误选其他服务商，并沿用 默认提示词。
    /// </summary>
    [Fact]
    public async Task GetAsync_ImageWithoutDefault_UsesOriginalDefaultPrompt()
    {
        using var db = new TestDatabase();
        await db.InsertProviderAsync("image-inactive", new
        {
            id = "image-inactive", stage = "image", enabled = false, @default = true,
            adapter = "worker", endpoint = "https://disabled.example/api", model = "disabled", api_key_cipher = "unused"
        });

        var result = await new SqlAiGenerationSettingsReader(db.CreateConnection).GetAsync("image");

        Assert.Null(result.Provider);
        Assert.Equal(1, result.PromptVersion);
        Assert.Equal("image", result.Stage);
        Assert.Contains("只输出一张图片", result.PromptTemplate);
        Assert.Contains("儿童画", result.PromptTemplate);
    }

    /// <summary>
    /// 管理员仅配置提示词而未启用服务商时，仍应正确返回自定义提示词。
    /// </summary>
    [Fact]
    public async Task GetAsync_ImageWithoutProvider_UsesCustomPrompt()
    {
        using var db = new TestDatabase();
        await db.InsertPromptAsync("image", new { id = "image", version = 3, template = "单张图片专用的定制提示词" });

        var result = await new SqlAiGenerationSettingsReader(db.CreateConnection).GetAsync("image");

        Assert.Null(result.Provider);
        Assert.Equal(3, result.PromptVersion);
        Assert.Equal("单张图片专用的定制提示词", result.PromptTemplate);
    }

    /// <summary>
    /// 模型阶段必须使用 model 配置，不能错误复用 image 模型或提示词。
    /// </summary>
    [Fact]
    public async Task GetAsync_Model_SelectsModelStageWithDefaultPrompt()
    {
        using var db = new TestDatabase();
        await db.InsertProviderAsync("image-default", new
        {
            id = "image-default", stage = "image", enabled = true, @default = true,
            adapter = "seedream", endpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations",
            model = "image-v1", api_key_cipher = "unused"
        });
        await db.InsertProviderAsync("model-default", new
        {
            id = "model-default", stage = "model", enabled = true, @default = true,
            adapter = "hunyuan3d", endpoint = "https://api.ai3d.cloud.tencent.com/v1/ai3d/submit",
            model = "3.0", api_key_cipher = "model-cipher"
        });

        var result = await new SqlAiGenerationSettingsReader(db.CreateConnection).GetAsync("model");

        Assert.Equal("model", result.Stage);
        Assert.Equal("model-default", result.Provider?.Id);
        Assert.Equal("hunyuan3d", result.Provider?.Adapter);
        Assert.Equal(1, result.PromptVersion);
        Assert.Contains("GLB", result.PromptTemplate);
        Assert.Contains("可3D打印", result.PromptTemplate);
    }

    /// <summary>
    /// 拒绝未定义的任务阶段，避免把其他配置当作图片或模型任务使用。
    /// </summary>
    [Fact]
    public async Task GetAsync_UnknownStage_Throws()
    {
        using var db = new TestDatabase();
        var reader = new SqlAiGenerationSettingsReader(db.CreateConnection);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.GetAsync("video"));
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "tongyin-ai-settings-" + Guid.NewGuid().ToString("N") + ".db");

        public TestDatabase()
        {
            using var db = CreateConnection();
            db.Open();
            db.Execute("CREATE TABLE ty_ai_providers (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);" +
                "CREATE TABLE ty_ai_prompts (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL)");
        }

        public DbConnection CreateConnection() => new SqliteConnection($"Data Source={_dbPath};Pooling=False");

        public async Task InsertProviderAsync(string id, object body)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ty_ai_providers (id,owner_id,created_at,payload) VALUES (@Id, @Owner, @CreatedAt, @Payload)",
                new { Id = id, Owner = "system", CreatedAt = "2026-10-15T10:00:00+08:00", Payload = JsonSerializer.Serialize(body) }));
        }

        public async Task InsertPromptAsync(string stage, object body)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ty_ai_prompts (id,owner_id,created_at,payload) VALUES (@Id, @Owner, @CreatedAt, @Payload)",
                new { Id = stage, Owner = "system", CreatedAt = "2026-10-15T10:00:00+08:00", Payload = JsonSerializer.Serialize(body) }));
        }

        public void Dispose()
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
    }
}
