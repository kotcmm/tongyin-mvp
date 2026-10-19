namespace Tongyin.Application.Works;

/// <summary>管理可恢复的混元 3D 提交 JobId 及真实任务状态。</summary>
public interface IModelGenerationJobRepository
{
    /// <summary>读取下一个 WAIT/RUN 的已提交任务，不允许返回还没有 JobId 的认领记录。</summary>
    Task<ModelGenerationJob?> GetNextSubmittedAsync(CancellationToken ct = default);

    /// <summary>保存混元返回的 JobId；只有 submitting 状态可首次写入。</summary>
    Task RecordSubmittedAsync(string generationId, string providerJobId, CancellationToken ct = default);

    /// <summary>保存真实 WAIT/RUN/DONE/FAIL 结果，不得把终态改回运行态。</summary>
    Task UpdateStatusAsync(string generationId, string status, CancellationToken ct = default);
}
