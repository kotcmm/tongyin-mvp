using Tongyin.Application.Ai;

namespace Tongyin.Application.Works;

/// <summary>
/// 图片生成的唯一应用业务入口：受理请求、调度待处理任务、认领、调用模型与回写图片。
/// 数据库事务、HTTP 请求及本地文件操作由各自基础设施适配器实现。
/// </summary>
public sealed class ImageGenerationService(
    IWorkRepository works,
    IAiGenerationSettingsReader settingsReader,
    IQueuedImageGenerationReader queuedReader,
    IOriginalImageReader originalImages,
    IImageGenerationClient generator,
    IGeneratedImageStore generatedImages)
{
    /// <summary>
    /// HTTP 阶段只登记生成任务，核实服务可用性和当前用户归属，不触发可能收费的模型调用。
    /// </summary>
    public async Task<WorkSnapshot?> QueueAsync(
        string owner, string workId, int freeImageAttempts, CancellationToken cancellationToken = default)
    {
        var settings = await settingsReader.GetAsync("image", cancellationToken);
        if (!settings.CanGenerateImage())
        {
            throw new ImageGenerationUnavailableException();
        }

        return await works.RegisterImageGenerationAsync(
            workId, owner, Guid.NewGuid().ToString("N"), freeImageAttempts, cancellationToken);
    }

    /// <summary>
    /// 每轮最多处理一笔已登记任务；认领与计费前验证配置并读取有限候选集。
    /// 上游结果不确定时不撤销认领，也不重试付费请求。
    /// </summary>
    public async Task<WorkSnapshot?> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsReader.GetAsync("image", cancellationToken);
        if (!settings.CanGenerateImage())
        {
            return null;
        }

        var queued = await queuedReader.ListAsync(1, cancellationToken);
        if (queued.Count == 0)
        {
            return null;
        }

        return await ProcessClaimedAsync(queued[0], settings, cancellationToken);
    }

    /// <summary>
    /// 业务关键顺序：原子认领 → 原画读取 → 模型调用 → 下载图片 → 原子回写。
    /// 已认领/过期的候选任务在原子认领失败时直接跳过，不再触发外部服务。
    /// </summary>
    private async Task<WorkSnapshot?> ProcessClaimedAsync(
        QueuedImageGeneration job, AiGenerationSettings settings, CancellationToken cancellationToken)
    {
        var work = await works.ClaimImageGenerationAsync(
            job.WorkId, job.Owner, job.GenerationId, DateTimeOffset.UtcNow, cancellationToken);
        if (work is null)
        {
            return null;
        }

        byte[] original;
        try
        {
            original = await originalImages.ReadImageAsync(work.Original, cancellationToken);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return await works.FailImageGenerationAsync(
                job.WorkId, job.GenerationId, "无法读取儿童原画", cancellationToken);
        }

        // Seedream 网络请求及结果下载失败可能已经产生费用。
        // 必须保留数据库中的原子认领状态，交由后台异常核查人工恢复。
        var imageUrl = await generator.GenerateAsync(settings, original, cancellationToken);
        var savedFile = await generatedImages.DownloadAsync(imageUrl, cancellationToken);
        try
        {
            return await works.CompleteImageGenerationAsync(
                job.WorkId, job.GenerationId, savedFile, cancellationToken);
        }
        catch
        {
            generatedImages.Delete(savedFile);
            throw;
        }
    }
}
