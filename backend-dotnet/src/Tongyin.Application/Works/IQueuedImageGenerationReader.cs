namespace Tongyin.Application.Works;

/// <summary>
/// 只读发现 .NET 已登记但尚未执行的图片任务；执行资格仍由仓储原子认领决定。
/// </summary>
public interface IQueuedImageGenerationReader
{
    /// <summary>
    /// 按最早登记时间读取有限数量的待派发任务，不修改作品记录。
    /// </summary>
    Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default);
}
