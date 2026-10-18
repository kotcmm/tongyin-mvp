namespace Tongyin.Application.Works;

/// <summary>运营人员查询等待核实的 3D 模型生成异常，不负责重试或计费。</summary>
public sealed class ListStalledModelTasks(IStalledModelTasksReader reader)
{
    /// <summary>列出超过指定阈值的已认领模型任务。</summary>
    public Task<IReadOnlyList<StalledModelTask>> ExecuteAsync(DateTimeOffset startedBefore, int limit = 100, CancellationToken cancellationToken = default)
        => reader.ListAsync(startedBefore, limit, cancellationToken);
}
