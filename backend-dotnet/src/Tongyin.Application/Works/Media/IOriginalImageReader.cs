namespace Tongyin.Application.Works;

/// <summary>
/// 为图片生成任务读取已经保存的用户原画，而不暴露本地媒体目录路径。
/// </summary>
public interface IOriginalImageReader
{
    /// <summary>
    /// 根据安全的媒体文件名读取不超过 10 MiB 的原画内容。
    /// </summary>
    Task<byte[]> ReadImageAsync(string fileName, CancellationToken cancellationToken = default);
}
