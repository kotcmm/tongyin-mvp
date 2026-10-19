namespace Tongyin.Application.Works;

/// <summary>
/// 轮询一次待生成任务并交给现有图片执行用例，避免在调度层重复实现认领、计费和回写。
/// </summary>
public sealed class DispatchQueuedImageGeneration(IQueuedImageGenerationReader queuedReader, ExecuteImageGeneration executor)
{
    /// <summary>
    /// 每轮最多派发一笔任务；由执行用例在数据库中原子认领，异常交由宿主记录。
    /// </summary>
    public async Task DispatchNextAsync(CancellationToken cancellationToken = default)
    {
        var queued = await queuedReader.ListAsync(1, cancellationToken);
        foreach (var task in queued)
        {
            await executor.ExecuteAsync(task.Owner, task.WorkId, task.GenerationId, cancellationToken);
        }
    }
}
