namespace Tongyin.Application.Works;

/// <summary>核实模型服务商结果后进行人工失败登记或真实 GLB 补录。</summary>
public sealed class ReviewStalledModelTask(IAdminModelReviewRepository repository, IModelUploadStore modelStore)
{
    /// <summary>确认模型服务商确实未生成模型，记录失败并保留已选择的卡通图。</summary>
    public Task<WorkSnapshot> ConfirmFailureAsync(string workId, string generationId, string actor, string reason, CancellationToken cancellationToken = default)
    {
        ValidateReview(workId, generationId, actor, reason);
        return repository.ResolveAsync(workId, generationId, actor, reason.Trim(), null, DateTimeOffset.UtcNow, cancellationToken);
    }

    /// <summary>验证上传的真实 GLB，再原子更新当前模型任务；更新失败回收孤立文件。</summary>
    public async Task<WorkSnapshot> CompleteFromUploadAsync(string workId, string generationId, string actor, string reason, Stream model, long length, CancellationToken cancellationToken = default)
    {
        ValidateReview(workId, generationId, actor, reason);
        ArgumentNullException.ThrowIfNull(model);
        if (length is <= 0 or > 100L * 1024 * 1024)
        {
            throw new InvalidDataException("GLB 模型必须在 100 MiB 以内");
        }

        var fileName = await modelStore.SaveGlbAsync(model, length, cancellationToken);
        try
        {
            return await repository.ResolveAsync(workId, generationId, actor, reason.Trim(), fileName, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch
        {
            modelStore.Delete(fileName);
            throw;
        }
    }

    private static void ValidateReview(string workId, string generationId, string actor, string reason)
    {
        if (string.IsNullOrWhiteSpace(workId) || string.IsNullOrWhiteSpace(generationId) || string.IsNullOrWhiteSpace(actor)
            || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 300)
        {
            throw new ArgumentException("需提供作品、任务编号及不超过 300 字的真实核实依据");
        }
    }
}
