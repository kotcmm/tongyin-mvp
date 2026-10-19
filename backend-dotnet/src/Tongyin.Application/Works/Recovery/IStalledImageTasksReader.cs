namespace Tongyin.Application.Works;

/// <summary>
/// 跨作品查询已认领但超时的图片生成任务，只读，不执行或重新派发任务。
/// </summary>
public interface IStalledImageTasksReader
{
    /// <summary>
    /// 返回开始时间不晚于指定时刻、仍未完成的已认领图片任务，按最早开始时间排序。
    /// </summary>
    Task<IReadOnlyList<StalledImageTask>> ListAsync(DateTimeOffset startedBefore, int limit, CancellationToken cancellationToken = default);
}
