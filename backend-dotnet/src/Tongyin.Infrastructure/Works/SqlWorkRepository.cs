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
}
