namespace Tongyin.Application.Works;

/// <summary>
/// 管理员人工核查图片任务的原子回写端口，负责同时提交作品变更和 Go 兼容审计。
/// </summary>
public interface IAdminImageReviewRepository
{
    /// <summary>
    /// 核实并处理当前已认领、超时的图片任务。文件名为空表示确认失败，否则表示补录成功。
    /// </summary>
    Task<WorkSnapshot> ResolveAsync(string workId, string generationId, string actor, string reason, string? fileName, DateTimeOffset reviewedAt, CancellationToken cancellationToken = default);
}
