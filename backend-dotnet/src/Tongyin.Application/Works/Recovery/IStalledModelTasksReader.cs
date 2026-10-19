namespace Tongyin.Application.Works;

/// <summary>只读发现当前已经认领但生成超时的模型任务。</summary>
public interface IStalledModelTasksReader
{
    /// <summary>列出开始时间不晚于指定阈值的模型任务，最多 100 条。</summary>
    Task<IReadOnlyList<StalledModelTask>> ListAsync(DateTimeOffset startedBefore, int limit, CancellationToken cancellationToken = default);
}
