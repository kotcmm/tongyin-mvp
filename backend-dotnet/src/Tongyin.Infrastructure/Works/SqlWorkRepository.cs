using System.Data.Common;
using Dapper;
using System.Globalization;
using System.Text.Json;
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
