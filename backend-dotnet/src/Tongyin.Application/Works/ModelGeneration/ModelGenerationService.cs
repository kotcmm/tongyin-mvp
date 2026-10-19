using Tongyin.Application.Ai;

namespace Tongyin.Application.Works;

/// <summary>
/// 3D 生成唯一业务入口：认领、图片转 Base64 提交混元、持久化 JobId、轮询、下载 GLB、结果回写。
/// 定时器只触发此服务；不需要独立 Worker、回调或公网 PUBLIC_URL。
/// </summary>
public sealed class ModelGenerationService(
    IQueuedModelGenerationReader queue, IModelGenerationClaimRepository claims,
    IModelGenerationJobRepository jobs, IAiGenerationSettingsReader settingsReader,
    IOriginalImageReader images, IHunyuan3DClient hunyuan,
    IGeneratedModelStore models, IModelGenerationResultRepository results)
{
    /// <summary>一轮优先查询已提交的 JobId；没有已提交任务才认领一笔新任务，避免重复付费。</summary>
    public async Task ProcessNextAsync(CancellationToken ct = default)
    {
        var settings = await settingsReader.GetAsync("model", ct);
        if (!settings.CanGenerateModel()) return;

        var active = await jobs.GetNextSubmittedAsync(ct);
        if (active is not null)
        {
            await PollAsync(active, settings, ct);
            return;
        }

        var queued = await queue.ListAsync(1, ct);
        if (queued.Count == 0) return;
        var task = queued[0];
        var work = await claims.ClaimAsync(task.WorkId, task.Owner, task.GenerationId,
            settings.Provider!.Id, settings.PromptVersion, DateTimeOffset.UtcNow, ct);
        if (work is null) return;

        // 认领与状态“submitting”已经持久化；即便网络中断，也不能自动二次收费。
        var image = await images.ReadImageAsync(work.Candidates[0], ct);
        var jobId = await hunyuan.SubmitAsync(settings, image, ct);
        await jobs.RecordSubmittedAsync(task.GenerationId, jobId, ct);
    }

    /// <summary>查询已持久化的 JobId，WAIT/RUN 只更新状态；DONE 下载 GLB，FAIL 登记失败。</summary>
    private async Task PollAsync(ModelGenerationJob job, AiGenerationSettings settings, CancellationToken ct)
    {
        var result = await hunyuan.QueryAsync(settings, job.ProviderJobId, ct);
        switch (result.Status)
        {
            case "WAIT":
            case "RUN":
                await jobs.UpdateStatusAsync(job.GenerationId, result.Status, ct);
                return;
            case "FAIL":
                await results.FailModelGenerationAsync(job.WorkId, job.GenerationId,
                    string.IsNullOrWhiteSpace(result.Error) ? "混元 3D 任务失败" : result.Error, ct);
                return;
            case "DONE" when !string.IsNullOrWhiteSpace(result.GlbUrl):
                var saved = await models.DownloadAsync(result.GlbUrl, ct);
                try
                {
                    await results.CompleteModelGenerationAsync(job.WorkId, job.GenerationId, saved, ct);
                }
                catch
                {
                    // 结果事务失败时，刚下载的文件还没有绑定作品，删除孤儿文件。
                    models.Delete(saved);
                    throw;
                }
                return;
            default:
                throw new InvalidDataException("混元返回了不支持的 3D 生成状态或没有 GLB 文件");
        }
    }
}
