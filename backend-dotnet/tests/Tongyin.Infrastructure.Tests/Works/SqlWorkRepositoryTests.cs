using System.Data.Common;
using System.Text.Json;
using Dapper;
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
        var useCase = new CreateWork(new SqlWorkRepository(db.CreateConnection), new UnusedImageStore());
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
        var useCase = new CreateWork(new SqlWorkRepository(db.CreateConnection), new UnusedImageStore());
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
        var useCase = new CreateWork(new SqlWorkRepository(db.CreateConnection), new UnusedImageStore());
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
        var useCase = new CreateWork(new SqlWorkRepository(db.CreateConnection), new UnusedImageStore());

        await Assert.ThrowsAnyAsync<DbException>(() => useCase.ExecuteAsync("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png"));
    }

    /// <summary>
    /// 作品列表必须按 owner_id 过滤数据库行，并还原 Go 已有图片状态和媒体文件名。
    /// </summary>
    [Fact]
    public async Task ListByOwnerAsync_ReturnsOnlyOwnedWorkWithStoredState()
    {
        using var db = new TestWorkDatabase(createTable: true);
        using (var connection = db.CreateConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES (@Id,@Owner,@CreatedAt,@Payload)",
                new[]
                {
                    new { Id = "work-001", Owner = "openid-001", CreatedAt = "2026-10-15T10:00:00+08:00",
                        Payload = JsonSerializer.Serialize(new { id = "work-001", created_at = "2026-10-15T10:00:00+08:00", original = "drawing.png", candidates = new[] { "cartoon.png" }, selected = -1, preview = new[] { "preview.png" }, model = "figure.glb", status = "model_ready", image_attempts = 2, generation_id = "job-001", note = "已完成" }) },
                    new { Id = "work-002", Owner = "openid-002", CreatedAt = "2026-10-15T11:00:00+08:00",
                        Payload = JsonSerializer.Serialize(new { id = "work-002", created_at = "2026-10-15T11:00:00+08:00", original = "private.png", status = "image_pending" }) }
                }));
        }

        var results = await new SqlWorkRepository(db.CreateConnection).ListByOwnerAsync("openid-001");

        var work = Assert.Single(results);
        Assert.Equal("work-001", work.Id);
        Assert.Equal("model_ready", work.Status);
        Assert.Equal("drawing.png", work.Original);
        Assert.Equal("cartoon.png", Assert.Single(work.Candidates));
        Assert.Equal("preview.png", Assert.Single(work.Preview));
        Assert.Equal("figure.glb", work.Model);
        Assert.Equal(2, work.ImageAttempts);
        Assert.Equal("job-001", work.GenerationId);
        Assert.Equal("已完成", work.Note);
    }

    /// <summary>
    /// 无匹配记录时，列表返回空数组而不是空值。
    /// </summary>
    [Fact]
    public async Task ListByOwnerAsync_WithoutWorks_ReturnsEmpty()
    {
        using var db = new TestWorkDatabase(createTable: true);

        var works = await new SqlWorkRepository(db.CreateConnection).ListByOwnerAsync("openid-none");

        Assert.Empty(works);
    }

    /// <summary>
    /// 按作品 ID 和 OpenID 双重筛选，返回现有 Go JSON 中的状态和模型文件名。
    /// </summary>
    [Fact]
    public async Task GetByIdForOwnerAsync_OwnWork_RestoresSnapshot()
    {
        using var db = new TestWorkDatabase(createTable: true);
        using (var connection = db.CreateConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ty_works (id,owner_id,created_at,payload) VALUES (@Id,@Owner,@CreatedAt,@Payload)",
                new { Id = "work-001", Owner = "openid-001", CreatedAt = "2026-10-15T10:00:00+08:00",
                    Payload = JsonSerializer.Serialize(new
                    {
                        id = "work-001", created_at = "2026-10-15T10:00:00+08:00",
                        original = "drawing.png", status = "model_ready",
                        model = "figure.glb", image_attempts = 2, generation_id = "job-001"
                    }) }));
        }

        var work = await new SqlWorkRepository(db.CreateConnection).GetByIdForOwnerAsync("work-001", "openid-001");

        Assert.NotNull(work);
        Assert.Equal("work-001", work.Id);
        Assert.Equal("model_ready", work.Status);
        Assert.Equal("figure.glb", work.Model);
        Assert.Equal(2, work.ImageAttempts);
        Assert.Equal("job-001", work.GenerationId);
    }

    /// <summary>
    /// 即使 ID 存在，其他微信用户也不能查询作品详情。
    /// </summary>
    [Fact]
    public async Task GetByIdForOwnerAsync_ForeignOwner_ReturnsNull()
    {
        using var db = new TestWorkDatabase(createTable: true);
        using (var connection = db.CreateConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ty_works (id,owner_id,created_at,payload) VALUES (@Id,@Owner,@CreatedAt,@Payload)",
                new { Id = "work-001", Owner = "openid-001", CreatedAt = "2026-10-15T10:00:00+08:00",
                    Payload = JsonSerializer.Serialize(new { id = "work-001", original = "private.png" }) }));
        }

        var work = await new SqlWorkRepository(db.CreateConnection).GetByIdForOwnerAsync("work-001", "openid-002");

        Assert.Null(work);
    }

    /// <summary>
    /// 查询不存在的 ID 返回空值。
    /// </summary>
    [Fact]
    public async Task GetByIdForOwnerAsync_MissingWork_ReturnsNull()
    {
        using var db = new TestWorkDatabase(createTable: true);

        var work = await new SqlWorkRepository(db.CreateConnection).GetByIdForOwnerAsync("missing", "openid-001");

        Assert.Null(work);
    }

    /// <summary>
    /// 登记任务必须同时持久化次数、生成编号和状态，并保留未触碰的 Go JSON 元数据。
    /// </summary>
    [Fact]
    public async Task RegisterImageGenerationAsync_FirstRequest_PersistsTaskAndPreservesMetadata()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_pending", selected = -1,
            image_attempts = 0, generation_id = "", note = "", ai_provider_id = "previous-config",
            candidates = Array.Empty<string>(), preview = Array.Empty<string>(), model = ""
        });

        var saved = await new SqlWorkRepository(db.CreateConnection).RegisterImageGenerationAsync(
            "work-001", "openid-001", "generation-001", 2);

        Assert.NotNull(saved);
        Assert.Equal("image_pending", saved.Status);
        Assert.Equal("generation-001", saved.GenerationId);
        Assert.Equal(1, saved.ImageAttempts);

        using var json = JsonDocument.Parse(await ReadWorkPayloadAsync(db, "work-001"));
        Assert.Equal("generation-001", json.RootElement.GetProperty("generation_id").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("image_attempts").GetInt32());
        Assert.Equal("previous-config", json.RootElement.GetProperty("ai_provider_id").GetString());
        Assert.Equal("drawing.png", json.RootElement.GetProperty("original").GetString());
    }

    /// <summary>
    /// 两个调用者同时登记同一作品，只能有一个成功，另一个必须被领域规则拒绝。
    /// </summary>
    [Fact]
    public async Task RegisterImageGenerationAsync_ConcurrentRequests_OnlyOneConsumesAttempt()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", selected = -1, status = "image_pending", image_attempts = 0
        });

        async Task<bool> TryRegisterAsync(string task)
        {
            try
            {
                await new SqlWorkRepository(db.CreateConnection).RegisterImageGenerationAsync(
                    "work-001", "openid-001", task, 2);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryRegisterAsync("generation-a"), TryRegisterAsync("generation-b"));

        Assert.Equal(1, results.Count(success => success));
        using var json = JsonDocument.Parse(await ReadWorkPayloadAsync(db, "work-001"));
        Assert.Equal(1, json.RootElement.GetProperty("image_attempts").GetInt32());
        Assert.Contains(json.RootElement.GetProperty("generation_id").GetString(), new[] { "generation-a", "generation-b" });
    }

    /// <summary>
    /// 跨用户请求与不存在的 ID 返回空值，且不修改原作品。
    /// </summary>
    [Fact]
    public async Task RegisterImageGenerationAsync_ForeignOrMissingWork_DoesNotChangeStoredPayload()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", selected = -1, status = "image_pending", image_attempts = 0
        });
        var before = await ReadWorkPayloadAsync(db, "work-001");
        var repository = new SqlWorkRepository(db.CreateConnection);

        Assert.Null(await repository.RegisterImageGenerationAsync("work-001", "openid-002", "generation-a", 2));
        Assert.Null(await repository.RegisterImageGenerationAsync("missing", "openid-001", "generation-b", 2));
        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 图片生成失败后允许最后一次重试，清除上一轮错误而保留其他 JSON 信息。
    /// </summary>
    [Fact]
    public async Task RegisterImageGenerationAsync_FailedImage_RetriesWithinQuota()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "failed", selected = -1, image_attempts = 1,
            generation_id = "generation-001", note = "上游失败",
            candidates = Array.Empty<string>(), preview = Array.Empty<string>(), model = ""
        });

        var updated = await new SqlWorkRepository(db.CreateConnection).RegisterImageGenerationAsync(
            "work-001", "openid-001", "generation-002", 2);

        Assert.NotNull(updated);
        Assert.Equal("image_pending", updated.Status);
        Assert.Equal(2, updated.ImageAttempts);
        Assert.Equal("generation-002", updated.GenerationId);
        Assert.Equal("", updated.Note);
        using var json = JsonDocument.Parse(await ReadWorkPayloadAsync(db, "work-001"));
        Assert.Equal("", json.RootElement.GetProperty("note").GetString());
    }

    /// <summary>
    /// 图片重新生成必须清除上一张候选图，不得破坏未触碰的其他属性。
    /// </summary>
    [Fact]
    public async Task RegisterImageGenerationAsync_ImageReady_ClearsOldCandidates()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_ready", selected = -1, image_attempts = 1,
            generation_id = "generation-001", note = "旧提示",
            candidates = new[] { "old.png" }, preview = Array.Empty<string>(), model = ""
        });

        var updated = await new SqlWorkRepository(db.CreateConnection).RegisterImageGenerationAsync(
            "work-001", "openid-001", "generation-002", 2);

        Assert.NotNull(updated);
        Assert.Empty(updated.Candidates);
        Assert.Equal(2, updated.ImageAttempts);
        using var json = JsonDocument.Parse(await ReadWorkPayloadAsync(db, "work-001"));
        Assert.Empty(json.RootElement.GetProperty("candidates").EnumerateArray());
        Assert.Equal("", json.RootElement.GetProperty("note").GetString());
    }

    /// <summary>
    /// 已经进入模型阶段时拒绝图片生成登记，数据库字段完全不变。
    /// </summary>
    [Fact]
    public async Task RegisterImageGenerationAsync_ModelPending_RejectsWithoutMutation()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "model_pending", selected = 0, image_attempts = 1,
            generation_id = "generation-001", note = "",
            candidates = new[] { "cartoon.png" }, preview = Array.Empty<string>(), model = ""
        });
        var before = await ReadWorkPayloadAsync(db, "work-001");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlWorkRepository(db.CreateConnection).RegisterImageGenerationAsync(
                "work-001", "openid-001", "generation-002", 2));

        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 免费次数不足时应拒绝登记，不额外消耗次数。
    /// </summary>
    [Fact]
    public async Task RegisterImageGenerationAsync_QuotaExhausted_RejectsWithoutMutation()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "failed", selected = -1, image_attempts = 2,
            generation_id = "generation-002", note = "失败"
        });
        var before = await ReadWorkPayloadAsync(db, "work-001");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlWorkRepository(db.CreateConnection).RegisterImageGenerationAsync(
                "work-001", "openid-001", "generation-003", 2));

        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 当前图片任务完成时持久化单张图片、状态与原任务编号，保留未修改的 Go JSON 字段。
    /// </summary>
    [Fact]
    public async Task CompleteImageGenerationAsync_CurrentTask_PersistsReadyAndPreservesJson()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_pending", selected = -1, image_attempts = 1,
            generation_id = "job-001", candidates = Array.Empty<string>(), note = "",
            ai_provider_id = "seedream-provider", ai_prompt_version = 5, custom_field = "保留"
        });

        var result = await new SqlWorkRepository(db.CreateConnection)
            .CompleteImageGenerationAsync("work-001", "job-001", "generated.png");

        Assert.Equal("image_ready", result.Status);
        Assert.Equal("generated.png", Assert.Single(result.Candidates));
        Assert.Equal("job-001", result.GenerationId);
        Assert.Equal(1, result.ImageAttempts);

        using var json = JsonDocument.Parse(await ReadWorkPayloadAsync(db, "work-001"));
        var value = json.RootElement;
        Assert.Equal("image_ready", value.GetProperty("status").GetString());
        Assert.Equal("generated.png", Assert.Single(value.GetProperty("candidates").EnumerateArray()).GetString());
        Assert.Equal("seedream-provider", value.GetProperty("ai_provider_id").GetString());
        Assert.Equal(5, value.GetProperty("ai_prompt_version").GetInt32());
        Assert.Equal("保留", value.GetProperty("custom_field").GetString());
    }

    /// <summary>
    /// 新任务已经登记时，旧任务的图片结果必须拒绝且不能改变记录。
    /// </summary>
    [Fact]
    public async Task CompleteImageGenerationAsync_OldGeneration_RejectsWithoutMutation()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_pending", selected = -1, image_attempts = 2,
            generation_id = "job-new", candidates = Array.Empty<string>()
        });
        var before = await ReadWorkPayloadAsync(db, "work-001");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlWorkRepository(db.CreateConnection).CompleteImageGenerationAsync("work-001", "job-old", "old.png"));

        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 已经完成的生成任务不得重复写入另一张图片。
    /// </summary>
    [Fact]
    public async Task CompleteImageGenerationAsync_DuplicateResult_RejectsWithoutMutation()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_pending", selected = -1, image_attempts = 1,
            generation_id = "job-001", candidates = Array.Empty<string>()
        });
        var repository = new SqlWorkRepository(db.CreateConnection);
        await repository.CompleteImageGenerationAsync("work-001", "job-001", "first.png");
        var before = await ReadWorkPayloadAsync(db, "work-001");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.CompleteImageGenerationAsync("work-001", "job-001", "duplicate.png"));

        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 图片生成已经失败或进入模型阶段时，不允许回写迟到的图片结果。
    /// </summary>
    [Theory]
    [InlineData("failed")]
    [InlineData("model_pending")]
    [InlineData("model_ready")]
    public async Task CompleteImageGenerationAsync_InvalidStatus_Rejects(string status)
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status, selected = status == "failed" ? -1 : 0,
            image_attempts = 1, generation_id = "job-001", candidates = Array.Empty<string>()
        });
        var before = await ReadWorkPayloadAsync(db, "work-001");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlWorkRepository(db.CreateConnection).CompleteImageGenerationAsync("work-001", "job-001", "late.png"));

        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 作品不存在或者没有登记有效任务编号时不得创建结果。
    /// </summary>
    [Fact]
    public async Task CompleteImageGenerationAsync_MissingOrUnregisteredWork_Rejects()
    {
        using var db = new TestWorkDatabase(createTable: true);
        var repository = new SqlWorkRepository(db.CreateConnection);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.CompleteImageGenerationAsync("missing", "job-001", "image.png"));

        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_pending", selected = -1, image_attempts = 0,
            generation_id = "", candidates = Array.Empty<string>()
        });
        var before = await ReadWorkPayloadAsync(db, "work-001");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.CompleteImageGenerationAsync("work-001", "", "image.png"));

        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 当前有效生成任务失败后仅更新状态与原因，保留原画、次数、编号和 AI 元数据。
    /// </summary>
    [Fact]
    public async Task FailImageGenerationAsync_CurrentTask_MarksFailedAndPreservesMetadata()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_pending", selected = -1,
            image_attempts = 1, generation_id = "job-001", note = "",
            candidates = Array.Empty<string>(), ai_provider_id = "seedream-a", ai_prompt_version = 3,
            custom_field = "保留"
        });

        var result = await new SqlWorkRepository(db.CreateConnection)
            .FailImageGenerationAsync("work-001", "job-001", "生成前配置不可用");

        Assert.Equal("failed", result.Status);
        Assert.Equal("生成前配置不可用", result.Note);
        Assert.Equal("job-001", result.GenerationId);
        Assert.Equal(1, result.ImageAttempts);

        using var json = JsonDocument.Parse(await ReadWorkPayloadAsync(db, "work-001"));
        var value = json.RootElement;
        Assert.Equal("failed", value.GetProperty("status").GetString());
        Assert.Equal("生成前配置不可用", value.GetProperty("note").GetString());
        Assert.Equal("drawing.png", value.GetProperty("original").GetString());
        Assert.Equal("seedream-a", value.GetProperty("ai_provider_id").GetString());
        Assert.Equal(3, value.GetProperty("ai_prompt_version").GetInt32());
        Assert.Equal("保留", value.GetProperty("custom_field").GetString());
    }

    /// <summary>
    /// 旧编号、重复失败及成功后迟到的失败事件均不能覆盖新状态。
    /// </summary>
    [Theory]
    [InlineData("image_pending", "job-new", "job-old")]
    [InlineData("failed", "job-001", "job-001")]
    [InlineData("image_ready", "job-001", "job-001")]
    [InlineData("model_pending", "job-001", "job-001")]
    [InlineData("image_pending", "", "")]
    public async Task FailImageGenerationAsync_StaleOrWrongStatus_RejectsWithoutMutation(string status, string storedJob, string submittedJob)
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status, selected = status == "model_pending" ? 0 : -1,
            image_attempts = 1, generation_id = storedJob, note = "原有状态",
            candidates = Array.Empty<string>()
        });
        var before = await ReadWorkPayloadAsync(db, "work-001");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlWorkRepository(db.CreateConnection).FailImageGenerationAsync(
                "work-001", submittedJob, "迟到的失败"));

        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 不存在的作品不能创建新的失败记录。
    /// </summary>
    [Fact]
    public async Task FailImageGenerationAsync_MissingWork_Rejects()
    {
        using var db = new TestWorkDatabase(createTable: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlWorkRepository(db.CreateConnection).FailImageGenerationAsync(
                "missing", "job-001", "不存在"));
    }

    /// <summary>
    /// 任务只允许首次原子认领，并记录当前编号、开始时间和阶段，不损坏旧版 Go 元数据。
    /// </summary>
    [Fact]
    public async Task ClaimImageGenerationAsync_FirstClaim_PersistsExecutionMarker()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_pending", selected = -1,
            image_attempts = 1, generation_id = "job-001", ai_execution_claim_id = "",
            ai_started_at = "", ai_task_stage = "", custom_data = "保留"
        });
        var repository = new SqlWorkRepository(db.CreateConnection);
        var started = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(8));

        var claimed = await repository.ClaimImageGenerationAsync("work-001", "openid-001", "job-001", started);

        Assert.NotNull(claimed);
        Assert.Equal("drawing.png", claimed.Original);
        Assert.Equal("job-001", claimed.AiExecutionClaimId);
        Assert.Equal("image_pending", claimed.Status);
        using var document = JsonDocument.Parse(await ReadWorkPayloadAsync(db, "work-001"));
        var value = document.RootElement;
        Assert.Equal("job-001", value.GetProperty("ai_execution_claim_id").GetString());
        Assert.Equal("image", value.GetProperty("ai_task_stage").GetString());
        Assert.Equal("2026-10-16T10:00:00+08:00", value.GetProperty("ai_started_at").GetString());
        Assert.Equal("保留", value.GetProperty("custom_data").GetString());
    }

    /// <summary>
    /// 同一任务在首次认领后不得再次认领，否则重启后会重复计费。
    /// </summary>
    [Fact]
    public async Task ClaimImageGenerationAsync_AlreadyClaimed_ReturnsNullWithoutMutation()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_pending", selected = -1,
            image_attempts = 1, generation_id = "job-001", ai_execution_claim_id = "job-001"
        });
        var repository = new SqlWorkRepository(db.CreateConnection);
        var before = await ReadWorkPayloadAsync(db, "work-001");

        var claimed = await repository.ClaimImageGenerationAsync("work-001", "openid-001", "job-001", DateTimeOffset.UtcNow);

        Assert.Null(claimed);
        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 外部 Go 执行器已经记录同一阶段开始时间时，不可再调用同一模型。
    /// </summary>
    [Fact]
    public async Task ClaimImageGenerationAsync_LegacyGoStartedTask_Rejects()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_pending", selected = -1,
            image_attempts = 1, generation_id = "job-001",
            ai_task_stage = "image", ai_started_at = "2026-10-16T09:00:00+08:00"
        });
        var before = await ReadWorkPayloadAsync(db, "work-001");

        Assert.Null(await new SqlWorkRepository(db.CreateConnection)
            .ClaimImageGenerationAsync("work-001", "openid-001", "job-001", DateTimeOffset.UtcNow));
        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 过期编号、其他用户及已完成作品均不得认领当前任务。
    /// </summary>
    [Theory]
    [InlineData("openid-001", "job-old", "image_pending")]
    [InlineData("openid-other", "job-001", "image_pending")]
    [InlineData("openid-001", "job-001", "image_ready")]
    [InlineData("openid-001", "job-001", "failed")]
    public async Task ClaimImageGenerationAsync_IneligibleRequest_ReturnsNull(string owner, string generationId, string status)
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status, selected = -1,
            image_attempts = 1, generation_id = "job-001", ai_execution_claim_id = ""
        });
        var before = await ReadWorkPayloadAsync(db, "work-001");

        var claimed = await new SqlWorkRepository(db.CreateConnection)
            .ClaimImageGenerationAsync("work-001", owner, generationId, DateTimeOffset.UtcNow);

        Assert.Null(claimed);
        Assert.Equal(before, await ReadWorkPayloadAsync(db, "work-001"));
    }

    /// <summary>
    /// 两个并发执行器只能有一个获取当前任务执行资格。
    /// </summary>
    [Fact]
    public async Task ClaimImageGenerationAsync_ConcurrentClaims_OnlyOneWins()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_pending", selected = -1,
            image_attempts = 1, generation_id = "job-001", ai_execution_claim_id = ""
        });
        async Task<bool> ClaimAsync()
        {
            var claimed = await new SqlWorkRepository(db.CreateConnection)
                .ClaimImageGenerationAsync("work-001", "openid-001", "job-001", DateTimeOffset.UtcNow);
            return claimed is not null;
        }

        var claimed = await Task.WhenAll(ClaimAsync(), ClaimAsync());

        Assert.Equal(1, claimed.Count(result => result));
    }

    /// <summary>
    /// 用户重新提交有效的图片生成尝试时，必须清理上一任务认领，允许新编号独立认领。
    /// </summary>
    [Fact]
    public async Task ClaimImageGenerationAsync_NewGeneration_CanClaimAfterRegistration()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", owner = "openid-001", created_at = "2026-10-15T10:00:00+08:00",
            original = "drawing.png", status = "image_ready", selected = -1,
            image_attempts = 1, generation_id = "job-001", ai_execution_claim_id = "job-001",
            ai_task_stage = "image", ai_started_at = "2026-10-15T11:00:00+08:00",
            candidates = new[] { "old.png" }
        });
        var repository = new SqlWorkRepository(db.CreateConnection);

        var registered = await repository.RegisterImageGenerationAsync("work-001", "openid-001", "job-002", 2);
        var claimed = await repository.ClaimImageGenerationAsync("work-001", "openid-001", "job-002", DateTimeOffset.UtcNow);

        Assert.NotNull(registered);
        Assert.NotNull(claimed);
        Assert.Equal("job-002", claimed.AiExecutionClaimId);
    }

    /// <summary>删除本人没有订单的已完成作品后，个人列表和详情不再返回记录。</summary>
    [Fact]
    public async Task DeleteAsync_OwnFinishedWork_RemovesRecord()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", status = "image_ready", original = "original.png", selected = -1,
            candidates = new[] { "image.png" }, created_at = "2026-10-15T10:00:00+08:00"
        });
        await CreateOrdersTableAsync(db);
        var repository = new SqlWorkRepository(db.CreateConnection);

        Assert.Equal(DeleteWorkResult.Deleted, await repository.DeleteWorkAsync("work-001", "openid-001"));
        Assert.Null(await repository.GetByIdForOwnerAsync("work-001", "openid-001"));
        Assert.Empty(await repository.ListByOwnerAsync("openid-001"));
        Assert.Equal(DeleteWorkResult.NotFound, await repository.DeleteWorkAsync("work-001", "openid-001"));
    }

    /// <summary>其他微信用户不允许删除目标作品，也不能得知目标作品是否存在。</summary>
    [Fact]
    public async Task DeleteAsync_ForeignOwner_ReturnsNotFoundAndKeepsWork()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", status = "failed", original = "original.png"
        });
        await CreateOrdersTableAsync(db);
        var repository = new SqlWorkRepository(db.CreateConnection);

        Assert.Equal(DeleteWorkResult.NotFound, await repository.DeleteWorkAsync("work-001", "openid-002"));
        Assert.NotNull(await repository.GetByIdForOwnerAsync("work-001", "openid-001"));
    }

    /// <summary>无论订单处于什么状态，已创建订单的作品不可硬删除，避免丢失履约关联。</summary>
    [Theory]
    [InlineData("awaiting_payment")]
    [InlineData("paid")]
    [InlineData("canceled")]
    public async Task DeleteAsync_OrderedWork_IsRejectedWithoutMutation(string status)
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", status = "model_ready", original = "original.png", model = "model.glb"
        });
        await CreateOrdersTableAsync(db);
        using (var connection = db.CreateConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO ty_orders(id,owner_id,created_at,payload) VALUES(@Id,@Owner,@Created,@Payload)",
                new { Id = "order-001", Owner = "openid-001", Created = "2026-10-15",
                    Payload = JsonSerializer.Serialize(new { id = "order-001", work_id = "work-001", status }) });
        }
        var repository = new SqlWorkRepository(db.CreateConnection);

        Assert.Equal(DeleteWorkResult.HasOrders, await repository.DeleteWorkAsync("work-001", "openid-001"));
        Assert.NotNull(await repository.GetByIdForOwnerAsync("work-001", "openid-001"));
    }

    /// <summary>AI 已认领的待生成作品不能删除，以免付费生成结束后发现作品消失。</summary>
    [Theory]
    [InlineData("image_pending", "job-001", "", "")]
    [InlineData("model_pending", "", "model", "2026-10-18T10:00:00+08:00")]
    [InlineData("image_pending", "", "", "2026-10-18T10:00:00+08:00")]
    public async Task DeleteAsync_InFlightGeneration_IsRejected(
        string status, string claimId, string stage, string startedAt)
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", status, ai_execution_claim_id = claimId,
            ai_task_stage = stage, ai_started_at = startedAt
        });
        await CreateOrdersTableAsync(db);

        var result = await new SqlWorkRepository(db.CreateConnection).DeleteWorkAsync("work-001", "openid-001");

        Assert.Equal(DeleteWorkResult.Processing, result);
        Assert.NotNull(await new SqlWorkRepository(db.CreateConnection).GetByIdForOwnerAsync("work-001", "openid-001"));
    }

    /// <summary>排队但未被认领的 .NET 任务可以取消；随后原子认领必须失败。</summary>
    [Fact]
    public async Task DeleteAsync_QueuedGeneration_RemovesPendingTaskSafely()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", status = "image_pending", generation_id = "job-001",
            ai_execution_claim_id = "", ai_task_stage = "", ai_started_at = ""
        });
        await CreateOrdersTableAsync(db);
        var repository = new SqlWorkRepository(db.CreateConnection);

        Assert.Equal(DeleteWorkResult.Deleted, await repository.DeleteWorkAsync("work-001", "openid-001"));
        Assert.Null(await repository.ClaimImageGenerationAsync("work-001", "openid-001", "job-001", DateTimeOffset.UtcNow));
    }

    /// <summary>旧 Go 待处理任务缺乏可信认领字段时不能冒险删除。</summary>
    [Fact]
    public async Task DeleteAsync_LegacyPendingWithoutClaimMetadata_RequiresReview()
    {
        using var db = new TestWorkDatabase(createTable: true);
        await SeedImageWorkAsync(db, "work-001", "openid-001", new
        {
            id = "work-001", status = "model_pending", generation_id = "legacy-job"
        });
        await CreateOrdersTableAsync(db);

        Assert.Equal(DeleteWorkResult.Processing, await new SqlWorkRepository(db.CreateConnection)
            .DeleteWorkAsync("work-001", "openid-001"));
    }

    private static async Task CreateOrdersTableAsync(TestWorkDatabase db)
    {
        using var connection = db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "CREATE TABLE ty_orders(id TEXT PRIMARY KEY,owner_id TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL)");
    }

    private static async Task SeedImageWorkAsync(TestWorkDatabase db, string id, string owner, object payload)
    {
        using var connection = db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO ty_works (id,owner_id,created_at,payload) VALUES (@Id,@Owner,@CreatedAt,@Payload)",
            new { Id = id, Owner = owner, CreatedAt = "2026-10-15T10:00:00+08:00",
                Payload = JsonSerializer.Serialize(payload) }));
    }

    private static async Task<string> ReadWorkPayloadAsync(TestWorkDatabase db, string id)
    {
        using var connection = db.CreateConnection();
        await connection.OpenAsync();
        return (await connection.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works WHERE id = @Id", new { Id = id })))!;
    }

    private sealed class UnusedImageStore : IOriginalImageStore
    {
        public Task<string> SaveImageAsync(Stream image, long length, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("这些测试验证已有原画的数据库持久化");

        public void Delete(string fileName) => throw new NotSupportedException();
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
            command.CommandText = "CREATE TABLE ty_works (id VARCHAR(32) PRIMARY KEY, owner_id VARCHAR(128) NOT NULL, created_at VARCHAR(40) NOT NULL, payload TEXT NOT NULL); CREATE TABLE ty_store_lock (id INTEGER PRIMARY KEY); INSERT INTO ty_store_lock (id) VALUES (1)";
            command.ExecuteNonQuery();
        }

        public DbConnection CreateConnection() => new SqliteConnection($"Data Source={_databaseFile};Pooling=False");

        public void Dispose()
        {
            if (File.Exists(_databaseFile)) File.Delete(_databaseFile);
        }
    }
}
