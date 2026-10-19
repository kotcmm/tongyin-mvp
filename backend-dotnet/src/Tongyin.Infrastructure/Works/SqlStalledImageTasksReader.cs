using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Works;

namespace Tongyin.Infrastructure.Works;

/// <summary>
/// 从作品表的 ty_works JSON 读取已认领且超时的图片任务，绝不修改原始记录。
/// </summary>
public sealed class SqlStalledImageTasksReader(Func<DbConnection> connectionFactory) : IStalledImageTasksReader
{
    /// <summary>
    /// 只筛选仍处于 image_pending 的已认领的 .NET 图片任务，返回最早的有限条目。
    /// </summary>
    public async Task<IReadOnlyList<StalledImageTask>> ListAsync(DateTimeOffset startedBefore, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "每次最多查询 100 个待核查任务");
        }

        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        // 作品状态保存在 TEXT JSON 中，不能使用只支持单一数据库的 JSON 操作符。
        var payloads = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works ORDER BY created_at DESC, id DESC",
            cancellationToken: cancellationToken));
        var stalled = new List<StalledImageTask>();
        foreach (var payload in payloads)
        {
            var work = JsonSerializer.Deserialize<WorkSnapshot>(payload)
                ?? throw new JsonException("作品记录不是合法的 JSON 对象");

            if (work.Status != "image_pending" || work.AiTaskStage != "image"
                || string.IsNullOrEmpty(work.GenerationId)
                || (!string.IsNullOrEmpty(work.AiExecutionClaimId)
                    && work.AiExecutionClaimId != work.GenerationId)
                || !DateTimeOffset.TryParse(work.AiStartedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startedAt)
                || startedAt > startedBefore)
            {
                continue;
            }

            stalled.Add(new StalledImageTask
            {
                WorkId = work.Id,
                GenerationId = work.GenerationId,
                StartedAt = startedAt,
                ImageAttempts = work.ImageAttempts,
                ProviderId = work.AiProviderId
            });
        }

        return stalled.OrderBy(item => item.StartedAt)
            .ThenBy(item => item.WorkId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }
}
