namespace Tongyin.Application.Works;

/// <summary>模型任务结果的原子写入端口，保证旧回调、重复回调不会覆盖当前作品。</summary>
public interface IModelGenerationResultRepository
{
    /// <summary>只在模型任务编号与状态匹配时保存已校验的 GLB 文件名。</summary>
    Task<WorkSnapshot> CompleteModelGenerationAsync(string workId, string generationId, string modelFileName, CancellationToken cancellationToken = default);

    /// <summary>只对当前模型任务登记失败原因，不破坏已确认卡通图。</summary>
    Task<WorkSnapshot> FailModelGenerationAsync(string workId, string generationId, string error, CancellationToken cancellationToken = default);
}
