namespace Tongyin.Application.Works;

/// <summary>在同一数据库事务内核实模型任务当前状态、更新作品及写入管理员审计。</summary>
public interface IAdminModelReviewRepository
{
    /// <summary>仅对已认领且超时的当前模型任务记录人工失败或关联已核实 GLB 文件。</summary>
    Task<WorkSnapshot> ResolveAsync(string workId, string generationId, string actor, string reason, string? fileName, DateTimeOffset reviewedAt, CancellationToken cancellationToken = default);
}
