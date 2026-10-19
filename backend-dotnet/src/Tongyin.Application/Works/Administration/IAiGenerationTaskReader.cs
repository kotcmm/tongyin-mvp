namespace Tongyin.Application.Works;

/// <summary>运营端读取当前两种生成阶段的任务清单，不能调用 AI 或修改任务状态。</summary>
public interface IAiGenerationTaskReader
{
    /// <summary>完整生命周期列表，不只查询超时记录。</summary>
    Task<IReadOnlyList<AiGenerationTask>> ListAsync(
        string stage, CancellationToken cancellationToken = default);
}
