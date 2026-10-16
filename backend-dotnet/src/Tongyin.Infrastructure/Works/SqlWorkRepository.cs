using System.Data.Common;
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
public sealed class SqlWorkRepository : IWorkRepository
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

    private static async Task AcquireStoreLockAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        // Go 使用同一 ty_store_lock 行；无意义的 UPDATE 兼容 SQLite、PostgreSQL 与 MySQL。
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
