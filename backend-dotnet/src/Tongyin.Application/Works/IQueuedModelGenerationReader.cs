namespace Tongyin.Application.Works;

/// <summary>只读发现尚未认领的模型生成任务，不执行网络请求。</summary>
public interface IQueuedModelGenerationReader
{
    /// <summary>按创建时间读取最多指定数量的待生成模型任务。</summary>
    Task<IReadOnlyList<QueuedModelGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default);
}
