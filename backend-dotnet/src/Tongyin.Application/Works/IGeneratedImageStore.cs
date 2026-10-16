namespace Tongyin.Application.Works;

/// <summary>
/// AI 生成图片的外部结果存储端口，负责下载并在回写失败时清理未关联文件。
/// </summary>
public interface IGeneratedImageStore
{
    /// <summary>
    /// 下载外部生成结果并保存为本地媒体文件，返回文件名。
    /// </summary>
    Task<string> DownloadAsync(string imageUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// 回收未关联到有效生成任务的本地媒体文件。
    /// </summary>
    void Delete(string fileName);
}
