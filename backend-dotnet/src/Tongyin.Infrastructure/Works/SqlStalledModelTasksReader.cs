using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Works;

namespace Tongyin.Infrastructure.Works;

/// <summary>从作品 JSON 查询已认领且超过 15 分钟的 3D 模型任务。</summary>
public sealed class SqlStalledModelTasksReader(Func<DbConnection> connectionFactory) : IStalledModelTasksReader
{
    /// <summary>只读扫描模型执行阶段，排除未认领、已完成、错误编号和未超时任务。</summary>
    public async Task<IReadOnlyList<StalledModelTask>> ListAsync(DateTimeOffset startedBefore, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        var payloads = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works ORDER BY created_at DESC, id DESC",
            cancellationToken: cancellationToken));
        var activeJobs = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT generation_id FROM ty_model_jobs WHERE status IN ('WAIT','RUN')",
            cancellationToken: cancellationToken))).ToHashSet(StringComparer.Ordinal);
        var stalled = new List<StalledModelTask>();
        foreach (var payload in payloads)
        {
            var work = JsonSerializer.Deserialize<WorkSnapshot>(payload)
                ?? throw new JsonException("作品记录不是合法的 JSON 对象");
            if (activeJobs.Contains(work.GenerationId) || work.Status != "model_pending" || work.Selected != 0 || work.Candidates.Count == 0
                || work.AiTaskStage != "model" || string.IsNullOrWhiteSpace(work.GenerationId)
                || (!string.IsNullOrEmpty(work.AiExecutionClaimId) && work.AiExecutionClaimId != work.GenerationId)
                || !DateTimeOffset.TryParse(work.AiStartedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startedAt)
                || startedAt > startedBefore)
            {
                continue;
            }

            stalled.Add(new StalledModelTask
            {
                WorkId = work.Id,
                GenerationId = work.GenerationId,
                StartedAt = startedAt,
                ImageAttempts = work.ImageAttempts,
                ProviderId = work.AiProviderId,
                IsLegacyGo = false
            });
        }

        return stalled.OrderBy(task => task.StartedAt)
            .ThenBy(task => task.WorkId, StringComparer.Ordinal)
            .Take(limit).ToArray();
    }
}
