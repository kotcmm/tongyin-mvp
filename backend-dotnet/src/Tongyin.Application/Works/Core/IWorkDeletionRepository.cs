namespace Tongyin.Application.Works;

/// <summary>带订单和 AI 认领保护的作品删除持久化边界。</summary>
public interface IWorkDeletionRepository
{
    /// <summary>以作品 ID 与微信用户标识执行原子删除；关联订单或执行中任务不能删除。</summary>
    Task<DeleteWorkResult> DeleteWorkAsync(string id, string owner, CancellationToken cancellationToken = default);
}
