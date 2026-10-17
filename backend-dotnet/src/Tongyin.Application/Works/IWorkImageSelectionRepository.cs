namespace Tongyin.Application.Works;

/// <summary>图片确认的独立持久化端口，避免通用作品读写接口承担无关操作。</summary>
public interface IWorkImageSelectionRepository
{
    /// <summary>确认本人就绪图片并原子切换模型阶段；作品不存在或不属于本人时返回空。</summary>
    Task<WorkSnapshot?> SelectImageAsync(string id, string owner, int index, CancellationToken cancellationToken = default);
}
