using System.Data.Common;
using Tongyin.Infrastructure.Media;
using Dapper;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;

namespace Tongyin.Infrastructure.Works;

/// <summary>
/// 基于 Dapper（轻量对象关系映射器）保存现有 ty_works 作品记录的 SQL 适配器。
/// 数据库连接由外围提供，支持不同 ADO.NET 驱动。
/// </summary>
public sealed class SqlWorkRepository : IWorkRepository, IWorkImageSelectionRepository, IModelGenerationResultRepository, IModelGenerationClaimRepository, IWorkDeletionRepository
{
    private readonly Func<DbConnection> _connectionFactory;

    /// <summary>
    /// 接收用于创建数据库连接的工厂；适配器负责关闭每次创建的连接。
    /// </summary>
    public SqlWorkRepository(Func<DbConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// 将新创建的作品插入兼容原 Go 后端 JSON 格式的 ty_works 记录。
    /// </summary>
    public async Task AddAsync(Work work, CancellationToken cancellationToken = default)
    {
        var createdAt = work.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var payload = JsonSerializer.Serialize(new
        {
            id = work.Id,
            owner = work.Owner,
            created_at = createdAt,
            original = work.Original,
            candidates = work.Candidates,
            selected = work.Selected,
            preview = work.Preview,
            model = work.Model,
            status = "image_pending",
            image_attempts = work.ImageAttempts,
            generation_id = work.GenerationId,
            note = work.Note
        });

        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO ty_works (id, owner_id, created_at, payload) VALUES (@Id, @OwnerId, @CreatedAt, @Payload)",
            new { Id = work.Id, OwnerId = work.Owner, CreatedAt = createdAt, Payload = payload },
            cancellationToken: cancellationToken));
    }
    /// <summary>
    /// 在与 Go 后端共用的 ty_store_lock 行锁事务中，登记图片生成请求。
    /// 先按 ID 和微信用户筛选并恢复领域对象，校验通过后一次性持久化所有状态变化。
    /// </summary>
    public async Task<WorkSnapshot?> RegisterImageGenerationAsync(string id, string owner, string generationId, int freeImageAttempts, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await AcquireStoreLockAsync(connection, transaction, cancellationToken);

        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works WHERE id = @Id AND owner_id = @Owner",
            new { Id = id, Owner = owner }, transaction: transaction, cancellationToken: cancellationToken));

        if (payload is null)
        {
            return null;
        }

        var restored = RestoreWork(id, owner, payload);

        // 领域实体负责状态和额度规则，仓储只承担原子持久化。
        restored.RequestImageGeneration(generationId, freeImageAttempts);

        var document = JsonNode.Parse(payload) as JsonObject
            ?? throw new JsonException("作品记录不是合法的 JSON 对象");
        document["status"] = "image_pending";
        document["image_attempts"] = restored.ImageAttempts;
        document["generation_id"] = restored.GenerationId;
        // 新生成编号允许重新认领；同时清除上一轮 Go/.NET 执行时间。
        document["ai_execution_claim_id"] = "";
        document["ai_started_at"] = "";
        document["ai_task_stage"] = "";
        document["candidates"] = new JsonArray();
        document["selected"] = restored.Selected;
        document["preview"] = new JsonArray();
        document["model"] = restored.Model;
        document["note"] = restored.Note;

        var updatedPayload = document.ToJsonString();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_works SET payload = @Payload WHERE id = @Id AND owner_id = @Owner",
            new { Payload = updatedPayload, Id = id, Owner = owner },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return JsonSerializer.Deserialize<WorkSnapshot>(updatedPayload)
            ?? throw new JsonException("更新后的作品记录不是合法的 JSON 对象");
    }

    /// <summary>
    /// 在与 Go 共用的事务锁下首次认领待生成图片，后续同编号的重复投递直接拒绝。
    /// 认领不可自动过期，以免 Seedream 调用结果不明时重复计费。
    /// </summary>
    public async Task<WorkSnapshot?> ClaimImageGenerationAsync(string id, string owner, string generationId, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(generationId))
        {
            return null;
        }

        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireStoreLockAsync(connection, transaction, cancellationToken);

        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works WHERE id = @Id AND owner_id = @Owner",
            new { Id = id, Owner = owner }, transaction: transaction, cancellationToken: cancellationToken));

        if (payload is null)
        {
            return null;
        }

        var work = JsonSerializer.Deserialize<WorkSnapshot>(payload)
            ?? throw new JsonException("作品记录不是合法的 JSON 对象");
        if (work.Status != "image_pending" || work.GenerationId != generationId
            || !string.IsNullOrEmpty(work.AiExecutionClaimId)
            || (work.AiTaskStage == "image" && !string.IsNullOrEmpty(work.AiStartedAt)))
        {
            return null;
        }

        var document = JsonNode.Parse(payload) as JsonObject
            ?? throw new JsonException("作品记录不是合法的 JSON 对象");
        document["ai_execution_claim_id"] = generationId;
        document["ai_task_stage"] = "image";
        document["ai_started_at"] = startedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var updated = document.ToJsonString();
        var result = JsonSerializer.Deserialize<WorkSnapshot>(updated)
            ?? throw new JsonException("更新后的作品记录不是合法的 JSON 对象");

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_works SET payload = @Payload WHERE id = @Id AND owner_id = @Owner",
            new { Payload = updated, Id = id, Owner = owner },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// 以与 Go 共用的事务写锁校验并提交当前图片任务结果，拒绝过期或重复回写。
    /// </summary>
    public async Task<WorkSnapshot> CompleteImageGenerationAsync(string id, string generationId, string imageFileName, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireStoreLockAsync(connection, transaction, cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<(string Owner, string Payload)>(new CommandDefinition(
            "SELECT owner_id AS Owner, payload AS Payload FROM ty_works WHERE id = @Id",
            new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));

        if (row.Payload is null)
        {
            throw new InvalidOperationException("生成任务已失效，无法保存形象图片");
        }

        var work = RestoreWork(id, row.Owner, row.Payload);
        work.CompleteImageGeneration(generationId, imageFileName);

        var document = JsonNode.Parse(row.Payload) as JsonObject
            ?? throw new JsonException("作品记录不是合法的 JSON 对象");
        document["status"] = "image_ready";
        document["candidates"] = new JsonArray(JsonValue.Create(imageFileName));
        var updatedPayload = document.ToJsonString();
        var result = JsonSerializer.Deserialize<WorkSnapshot>(updatedPayload)
            ?? throw new JsonException("更新后的作品记录不是合法的 JSON 对象");

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_works SET payload = @Payload WHERE id = @Id",
            new { Payload = updatedPayload, Id = id },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// 在数据库事务中只对当前有效的图片任务登记明确失败，其他业务字段保持不变。
    /// </summary>
    public async Task<WorkSnapshot> FailImageGenerationAsync(string id, string generationId, string failureReason, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireStoreLockAsync(connection, transaction, cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<(string Owner, string Payload)>(new CommandDefinition(
            "SELECT owner_id AS Owner, payload AS Payload FROM ty_works WHERE id = @Id",
            new { Id = id }, transaction: transaction, cancellationToken: cancellationToken));

        if (row.Payload is null)
        {
            throw new InvalidOperationException("生成任务已失效，无法记录失败结果");
        }

        var work = RestoreWork(id, row.Owner, row.Payload);
        work.FailImageGeneration(generationId, failureReason);

        var document = JsonNode.Parse(row.Payload) as JsonObject
            ?? throw new JsonException("作品记录不是合法的 JSON 对象");
        document["status"] = "failed";
        document["note"] = work.Note;
        var updatedPayload = document.ToJsonString();
        var result = JsonSerializer.Deserialize<WorkSnapshot>(updatedPayload)
            ?? throw new JsonException("更新后的作品记录不是合法的 JSON 对象");

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_works SET payload = @Payload WHERE id = @Id",
            new { Payload = updatedPayload, Id = id },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// 以同一作品事务锁校验选图状态，保护其他用户数据，并保证确认作品只保留一张候选图。
    /// </summary>
    public async Task<WorkSnapshot?> SelectImageAsync(string id, string owner, int index, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireStoreLockAsync(connection, transaction, cancellationToken);

        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works WHERE id = @Id AND owner_id = @Owner",
            new { Id = id, Owner = owner }, transaction: transaction, cancellationToken: cancellationToken));

        if (payload is null)
        {
            return null;
        }

        var work = RestoreWork(id, owner, payload);
        work.SelectImage(index);

        var document = JsonNode.Parse(payload) as JsonObject
            ?? throw new JsonException("作品记录不是合法的 JSON 对象");
        document["candidates"] = new JsonArray(JsonValue.Create(work.Candidates[0]));
        document["selected"] = work.Selected;
        document["preview"] = new JsonArray();
        document["model"] = "";
        document["status"] = "model_pending";
        document["note"] = "";
        document["ai_task_stage"] = "";
        document["ai_started_at"] = "";
        document["ai_execution_claim_id"] = "";
        // 图片阶段生成编号不可复用：每次确认进入 3D 必须分配新任务编号。
        document["generation_id"] = Guid.NewGuid().ToString("N");

        var updated = document.ToJsonString();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_works SET payload = @Payload WHERE id = @Id AND owner_id = @Owner",
            new { Payload = updated, Id = id, Owner = owner },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return JsonSerializer.Deserialize<WorkSnapshot>(updated)
            ?? throw new JsonException("更新后的作品记录不是合法的 JSON 对象");
    }

    /// <summary>原子保存当前模型生成任务的 GLB 文件名，拒绝重复或过期回调。</summary>
    public Task<WorkSnapshot> CompleteModelGenerationAsync(string workId, string generationId, string modelFileName, CancellationToken cancellationToken = default)
        => UpdateModelResultAsync(workId, generationId, modelFileName, null, cancellationToken);

    /// <summary>原子记录当前模型生成任务的明确失败，不改动已经确认的形象图片。</summary>
    public Task<WorkSnapshot> FailModelGenerationAsync(string workId, string generationId, string error, CancellationToken cancellationToken = default)
        => UpdateModelResultAsync(workId, generationId, null, error, cancellationToken);

    private async Task<WorkSnapshot> UpdateModelResultAsync(string workId, string generationId, string? modelFileName, string? error, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireStoreLockAsync(connection, transaction, cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<(string Owner, string Payload)>(new CommandDefinition(
            "SELECT owner_id AS Owner, payload AS Payload FROM ty_works WHERE id = @Id",
            new { Id = workId }, transaction: transaction, cancellationToken: cancellationToken));
        if (row.Payload is null)
        {
            throw new InvalidOperationException("模型任务已失效");
        }

        var work = RestoreWork(workId, row.Owner, row.Payload);
        if (modelFileName is not null)
        {
            work.CompleteModelGeneration(generationId, modelFileName);
        }
        else
        {
            work.FailModelGeneration(generationId, error ?? "");
        }

        var document = JsonNode.Parse(row.Payload) as JsonObject
            ?? throw new JsonException("作品记录不是合法的 JSON 对象");
        document["status"] = modelFileName is null ? "failed" : "model_ready";
        document["model"] = work.Model;
        document["note"] = work.Note;
        var updated = document.ToJsonString();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_works SET payload = @Payload WHERE id = @Id",
            new { Payload = updated, Id = workId },
            transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ty_model_jobs SET status=@Status, updated_at=@At
            WHERE generation_id=@Generation AND status IN ('WAIT','RUN','submitting')
            """, new { Status = modelFileName is null ? "FAIL" : "DONE",
                At = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), Generation = generationId },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return JsonSerializer.Deserialize<WorkSnapshot>(updated)
            ?? throw new JsonException("更新后的模型记录不是合法的 JSON 对象");
    }

    /// <summary>原子认领模型任务并创建混元任务记录，已认领的任务绝不重复提交。</summary>
    public async Task<WorkSnapshot?> ClaimAsync(string workId, string owner, string generationId, string providerId, int promptVersion, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireStoreLockAsync(connection, transaction, cancellationToken);

        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works WHERE id = @Id AND owner_id = @Owner",
            new { Id = workId, Owner = owner }, transaction: transaction, cancellationToken: cancellationToken));
        if (payload is null)
        {
            return null;
        }

        var document = JsonNode.Parse(payload) as JsonObject
            ?? throw new JsonException("作品记录不是合法的 JSON 对象");
        var work = JsonSerializer.Deserialize<WorkSnapshot>(payload)
            ?? throw new JsonException("作品记录不是合法的 JSON 对象");
        if (work.Status != "model_pending" || work.GenerationId != generationId
            || work.Selected != 0 || work.Candidates.Count == 0
            || !string.IsNullOrWhiteSpace(work.AiExecutionClaimId)
            || !string.IsNullOrWhiteSpace(work.AiTaskStage)
            || !string.IsNullOrWhiteSpace(work.AiStartedAt))
        {
            return null;
        }

        document["ai_execution_claim_id"] = generationId;
        document["ai_task_stage"] = "model";
        document["ai_started_at"] = startedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        document["ai_provider_id"] = providerId;
        document["ai_prompt_version"] = promptVersion;
        var updated = document.ToJsonString();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_works SET payload = @Payload WHERE id = @Id AND owner_id = @Owner",
            new { Payload = updated, Id = workId, Owner = owner },
            transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ty_model_jobs (generation_id,work_id,owner_id,provider_id,prompt_version,provider_job_id,status,created_at,updated_at)
            VALUES (@Generation,@Work,@Owner,@Provider,@Version,NULL,'submitting',@At,@At)
            """, new { Generation = generationId, Work = workId, Owner = owner, Provider = providerId,
                Version = promptVersion, At = startedAt.ToString("O", CultureInfo.InvariantCulture) },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return JsonSerializer.Deserialize<WorkSnapshot>(updated)
            ?? throw new JsonException("更新后的模型记录不是合法的 JSON 对象");
    }

    private static async Task AcquireStoreLockAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        // 跨数据库锁行确保作品认领和完成原子化。
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_store_lock SET id = id WHERE id = 1",
            transaction: transaction, cancellationToken: cancellationToken));
        await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT id FROM ty_store_lock WHERE id = 1",
            transaction: transaction, cancellationToken: cancellationToken));
    }

    private static Work RestoreWork(string id, string owner, string payload)
    {
        var snapshot = JsonSerializer.Deserialize<WorkSnapshot>(payload)
            ?? throw new JsonException("作品记录不是合法的 JSON 对象");
        var status = snapshot.Status switch
        {
            "image_pending" => WorkStatus.ImagePending,
            "image_ready" => WorkStatus.ImageReady,
            "model_pending" => WorkStatus.ModelPending,
            "model_ready" => WorkStatus.ModelReady,
            "failed" => WorkStatus.Failed,
            _ => throw new JsonException("作品状态无效")
        };

        return Work.Restore(
            id, owner, DateTimeOffset.Parse(snapshot.CreatedAt, CultureInfo.InvariantCulture),
            snapshot.Original, status, snapshot.Selected, snapshot.ImageAttempts,
            snapshot.GenerationId, snapshot.Candidates, snapshot.Model, snapshot.Note);
    }

    /// <summary>
    /// 删除当前微信用户未关联订单、且没有已执行 AI 任务的作品。
    /// 与创建订单及 AI 认领使用同一事务行锁，防止检查后被其他请求抢先更改。
    /// 不在数据库事务中删除媒体文件，避免 DB 回滚后出现作品引用的图片丢失。
    /// </summary>
    public async Task<DeleteWorkResult> DeleteWorkAsync(string id, string owner, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireStoreLockAsync(connection, transaction, cancellationToken);

        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works WHERE id=@Id AND owner_id=@Owner",
            new { Id = id, Owner = owner }, transaction: transaction, cancellationToken: cancellationToken));
        if (payload is null)
        {
            return DeleteWorkResult.NotFound;
        }

        // 订单 JSON 是跨 PostgreSQL/MySQL 兼容结构，因此在应用内解析 work_id，不使用数据库特定 JSON 方言。
        // 任何历史订单（包括取消订单）都需保留原作品，便于售后和争议处理。
        var orders = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders", transaction: transaction, cancellationToken: cancellationToken));
        foreach (var orderPayload in orders)
        {
            using var order = JsonDocument.Parse(orderPayload);
            if (order.RootElement.TryGetProperty("work_id", out var workId)
                && workId.ValueKind == JsonValueKind.String
                && workId.GetString() == id)
            {
                return DeleteWorkResult.HasOrders;
            }
        }

        using var data = JsonDocument.Parse(payload);
        var root = data.RootElement;
        if (root.TryGetProperty("status", out var statusElement)
            && statusElement.ValueKind == JsonValueKind.String
            && statusElement.GetString() is "image_pending" or "model_pending")
        {
            static bool Nonempty(JsonElement record, string key)
                => record.TryGetProperty(key, out var value) &&
                   (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));

            // 老 Go 未记录认领元数据，不能断言模型没有被提交；但尚未生成任务编号的原始草稿可删。
            var hasClaimMarker = root.TryGetProperty("ai_execution_claim_id", out _);
            if (Nonempty(root, "ai_execution_claim_id") || Nonempty(root, "ai_started_at")
                || Nonempty(root, "ai_task_stage")
                || (!hasClaimMarker && Nonempty(root, "generation_id")))
            {
                return DeleteWorkResult.Processing;
            }
        }

        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ty_works WHERE id=@Id AND owner_id=@Owner",
            new { Id = id, Owner = owner }, transaction: transaction, cancellationToken: cancellationToken));
        if (deleted != 1)
        {
            throw new InvalidOperationException("作品删除状态发生变化，请重试");
        }

        // 将本次作品引用的媒体登记到待清理队列；提交失败时记录和作品删除一起回滚。
        // 不直接删除磁盘：媒体可能被其它作品共用，且 DB 与文件系统不能形成同一个事务。
        using (var removed = JsonDocument.Parse(payload))
        {
            var deletedRoot = removed.RootElement;
            var files = new HashSet<string>(StringComparer.Ordinal);
            static void Remember(JsonElement parent, string property, HashSet<string> results)
            {
                if (!parent.TryGetProperty(property, out var item)) return;
                if (item.ValueKind == JsonValueKind.String)
                {
                    var name = item.GetString();
                    if (LocalMediaStore.IsStoredMediaName(name)) results.Add(name!);
                }
                else if (item.ValueKind == JsonValueKind.Array)
                {
                    foreach (var value in item.EnumerateArray())
                    {
                        if (value.ValueKind == JsonValueKind.String
                            && LocalMediaStore.IsStoredMediaName(value.GetString()))
                        {
                            results.Add(value.GetString()!);
                        }
                    }
                }
            }

            Remember(deletedRoot, "original", files);
            Remember(deletedRoot, "candidates", files);
            Remember(deletedRoot, "preview", files);
            Remember(deletedRoot, "model", files);

            foreach (var file in files)
            {
                var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM ty_media_cleanup WHERE file_name=@File",
                    new { File = file }, transaction: transaction, cancellationToken: cancellationToken));
                if (exists == 0)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO ty_media_cleanup(file_name,requested_at) VALUES(@File,@At)",
                        new { File = file, At = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
                        transaction: transaction, cancellationToken: cancellationToken));
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return DeleteWorkResult.Deleted;
    }

    /// <summary>
    /// 通过参数化的作品 ID 与微信用户标识联合查询，防止其他用户读取作品详情。
    /// </summary>
    public async Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works WHERE id = @Id AND owner_id = @Owner",
            new { Id = id, Owner = owner },
            cancellationToken: cancellationToken));

        return payload is null
            ? null
            : JsonSerializer.Deserialize<WorkSnapshot>(payload)
                ?? throw new JsonException("作品记录不是合法的 JSON 对象");
    }

    /// <summary>
    /// 按用户标识读取 Go 兼容作品 JSON，按创建时间及标识倒序返回。
    /// </summary>
    public async Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        var payloads = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works WHERE owner_id = @Owner ORDER BY created_at DESC, id DESC",
            new { Owner = owner },
            cancellationToken: cancellationToken));
        var results = new List<WorkSnapshot>();
        foreach (var payload in payloads)
        {
            results.Add(JsonSerializer.Deserialize<WorkSnapshot>(payload)
                ?? throw new JsonException("作品记录不是合法的 JSON 对象"));
        }

        return results;
    }

}
