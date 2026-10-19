namespace Tongyin.Application.Works;

/// <summary>
/// 管理员核查超时图片任务的只读应用用例，不包含任务重试或状态修改。
/// </summary>
public sealed class ListStalledImageTasks(IStalledImageTasksReader reader)
{
    /// <summary>
    /// 获取超过指定时间阈值的待核查任务。
    /// </summary>
    public Task<IReadOnlyList<StalledImageTask>> ExecuteAsync(DateTimeOffset startedBefore, int limit = 100, CancellationToken cancellationToken = default)
        => reader.ListAsync(startedBefore, limit, cancellationToken);
}
