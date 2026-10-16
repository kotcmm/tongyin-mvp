namespace Tongyin.Application.Works;

/// <summary>
/// 将单张 AI 生成图片保存到本地，并提交到当前有效的作品任务；失败时清理新下载的文件。
/// </summary>
public sealed class SaveImageGenerationResult(IWorkRepository repository, IGeneratedImageStore imageStore)
{
    /// <summary>
    /// 下载远端图片并以任务编号完成原子回写；任何回写异常都会触发本次文件的回收。
    /// </summary>
    public async Task<WorkSnapshot> ExecuteAsync(string workId, string generationId, string resultUrl, CancellationToken cancellationToken = default)
    {
        var fileName = await imageStore.DownloadAsync(resultUrl, cancellationToken);
        try
        {
            return await repository.CompleteImageGenerationAsync(workId, generationId, fileName, cancellationToken);
        }
        catch
        {
            imageStore.Delete(fileName);
            throw;
        }
    }
}
