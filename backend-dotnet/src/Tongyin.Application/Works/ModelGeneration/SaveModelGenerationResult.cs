namespace Tongyin.Application.Works;

/// <summary>处理模型服务回调，保存 GLB 并保证失败提交时清理新文件。</summary>
public sealed class SaveModelGenerationResult(IModelGenerationResultRepository repository, IGeneratedModelStore modelStore)
{
    /// <summary>验证下载生成结果并原子完成当前任务，写库失败时撤销刚下载的 GLB。</summary>
    public async Task<WorkSnapshot> CompleteAsync(string workId, string generationId, string modelUrl, CancellationToken cancellationToken = default)
    {
        var fileName = await modelStore.DownloadAsync(modelUrl, cancellationToken);
        try
        {
            return await repository.CompleteModelGenerationAsync(workId, generationId, fileName, cancellationToken);
        }
        catch
        {
            modelStore.Delete(fileName);
            throw;
        }
    }

    /// <summary>将服务商明确报告的失败登记到当前模型任务。</summary>
    public Task<WorkSnapshot> FailAsync(string workId, string generationId, string error, CancellationToken cancellationToken = default)
        => repository.FailModelGenerationAsync(workId, generationId, error, cancellationToken);
}
