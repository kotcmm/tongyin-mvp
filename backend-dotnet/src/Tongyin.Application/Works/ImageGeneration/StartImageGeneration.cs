namespace Tongyin.Application.Works;

/// <summary>
/// 生成新的图片任务标识，并交由仓储原子登记作品生成状态；本阶段不派发 AI 请求。
/// </summary>
public sealed class StartImageGeneration(IWorkRepository repository)
{
    /// <summary>
    /// 为当前微信用户的作品登记一次图片生成尝试，返回更新后的只读作品快照。
    /// </summary>
    public Task<WorkSnapshot?> ExecuteAsync(string owner, string id, int freeImageAttempts = 2, CancellationToken cancellationToken = default)
    {
        return repository.RegisterImageGenerationAsync(id, owner, Guid.NewGuid().ToString("N"), freeImageAttempts, cancellationToken);
    }
}
