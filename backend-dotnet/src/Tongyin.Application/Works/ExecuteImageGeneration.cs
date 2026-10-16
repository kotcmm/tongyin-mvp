using Tongyin.Application.Ai;

namespace Tongyin.Application.Works;

/// <summary>
/// 执行一笔已登记的图片生成任务：校验状态、取得原画与配置、调用模型并保存单张结果。
/// 此用例不负责新任务登记、任务调度及不确定结果的自动重试。
/// </summary>
public sealed class ExecuteImageGeneration(IWorkRepository repository, IAiGenerationSettingsReader settingsReader, IOriginalImageReader originalImageReader, IImageGenerationClient generator, SaveImageGenerationResult saveResult)
{
    /// <summary>
    /// 只执行指定微信用户当前有效的图片生成任务；提交模型前的明确失败写入 failed。
    /// 模型调用或后续下载结果不确定时保留 image_pending，交由后续核查机制处理。
    /// </summary>
    public async Task<WorkSnapshot> ExecuteAsync(string owner, string workId, string generationId, CancellationToken cancellationToken = default)
    {
        var work = await repository.GetByIdForOwnerAsync(workId, owner, cancellationToken);
        if (work is null || work.Status != "image_pending"
            || string.IsNullOrEmpty(generationId) || work.GenerationId != generationId)
        {
            throw new InvalidOperationException("生成任务已失效");
        }

        var settings = await settingsReader.GetAsync("image", cancellationToken);
        var provider = settings.Provider;
        if (settings.Stage != "image" || provider is null || provider.Stage != "image"
            || provider.Adapter != "seedream" || !provider.Enabled || !provider.Default)
        {
            return await repository.FailImageGenerationAsync(
                workId, generationId, "未配置可用的图片生成服务商", cancellationToken);
        }

        byte[] originalImage;
        try
        {
            originalImage = await originalImageReader.ReadImageAsync(work.Original, cancellationToken);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return await repository.FailImageGenerationAsync(
                workId, generationId, "无法读取儿童原画", cancellationToken);
        }

        // 从调用外部模型开始，网络超时或结果下载失败可能已发生计费，不能自动当作可重试失败。
        var imageUrl = await generator.GenerateAsync(settings, originalImage, cancellationToken);
        return await saveResult.ExecuteAsync(workId, generationId, imageUrl, cancellationToken);
    }
}
