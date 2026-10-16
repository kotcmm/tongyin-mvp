namespace Tongyin.Application.Works;

/// <summary>
/// 管理员手动处理超时图片任务，不调用模型，不修改认领标记；补录回写失败时回收文件。
/// </summary>
public sealed class ReviewStalledImageTask(IAdminImageReviewRepository repository, IOriginalImageStore imageStore)
{
    /// <summary>
    /// 根据服务商核实结果确认图片生成失败，处理依据仅写入管理员审计。
    /// </summary>
    public Task<WorkSnapshot> ConfirmFailureAsync(string workId, string generationId, string actor, string reason, CancellationToken cancellationToken = default)
    {
        ValidateReview(workId, generationId, actor, reason);
        return repository.ResolveAsync(workId, generationId, actor, reason.Trim(), null, DateTimeOffset.UtcNow, cancellationToken);
    }

    /// <summary>
    /// 把管理员上传的实际生成图片补录到当前任务，数据库拒绝时清理无关联媒体。
    /// </summary>
    public async Task<WorkSnapshot> CompleteFromUploadAsync(string workId, string generationId, string actor, string reason, Stream image, long length, CancellationToken cancellationToken = default)
    {
        ValidateReview(workId, generationId, actor, reason);
        ArgumentNullException.ThrowIfNull(image);
        if (length is <= 0 or > ImageFileLimits.MaxBytes)
        {
            throw new InvalidDataException("补录图片必须在 10 MiB 以内");
        }

        var fileName = await imageStore.SaveImageAsync(image, length, cancellationToken);
        try
        {
            return await repository.ResolveAsync(workId, generationId, actor, reason.Trim(), fileName, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch
        {
            imageStore.Delete(fileName);
            throw;
        }
    }

    private static void ValidateReview(string workId, string generationId, string actor, string reason)
    {
        if (string.IsNullOrWhiteSpace(workId) || string.IsNullOrWhiteSpace(generationId) || string.IsNullOrWhiteSpace(actor)
            || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 300)
        {
            throw new ArgumentException("需填写有效的作品、任务编号及不超过 300 字的核实依据");
        }
    }
}
