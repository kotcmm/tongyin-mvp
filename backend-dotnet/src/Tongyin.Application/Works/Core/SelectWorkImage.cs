namespace Tongyin.Application.Works;

/// <summary>将用户确认的唯一卡通图作为模型生成输入，原子推进作品业务状态。</summary>
public sealed class SelectWorkImage(IWorkImageSelectionRepository repository)
{
    /// <summary>仅当前用户可确认自己的已就绪图片，并返回更新后的作品快照。</summary>
    public Task<WorkSnapshot?> ExecuteAsync(string owner, string id, int index, CancellationToken cancellationToken = default)
        => repository.SelectImageAsync(id, owner, index, cancellationToken);
}
