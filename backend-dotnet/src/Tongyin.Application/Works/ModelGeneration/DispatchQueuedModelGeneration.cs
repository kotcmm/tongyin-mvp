using Tongyin.Application.Ai;

namespace Tongyin.Application.Works;

/// <summary>一次模型队列派发：先检查服务商，再原子认领，最后只提交一笔 worker 请求。</summary>
public sealed class DispatchQueuedModelGeneration(IQueuedModelGenerationReader queuedReader, IModelGenerationClaimRepository claimRepository, IAiGenerationSettingsReader settingsReader, IModelWorkerClient workerClient)
{
    /// <summary>没有合格服务商时不认领；网络结果不明时不自动重试已认领任务。</summary>
    public async Task DispatchNextAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsReader.GetAsync("model", cancellationToken);
        if (!settings.CanGenerateModel())
        {
            return;
        }

        var queued = await queuedReader.ListAsync(1, cancellationToken);
        foreach (var task in queued)
        {
            var claimed = await claimRepository.ClaimAsync(
                task.WorkId, task.Owner, task.GenerationId, settings.Provider!.Id,
                settings.PromptVersion, DateTimeOffset.UtcNow, cancellationToken);
            if (claimed is null)
            {
                continue;
            }

            await workerClient.SubmitAsync(settings, claimed, cancellationToken);
        }
    }
}
