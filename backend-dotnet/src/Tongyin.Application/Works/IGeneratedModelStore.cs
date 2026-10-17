namespace Tongyin.Application.Works;

/// <summary>安全下载、保存及清理 AI 生成的 GLB 模型文件。</summary>
public interface IGeneratedModelStore
{
    /// <summary>从公网 HTTPS 下载 GLB 并保存为本地媒体文件。</summary>
    Task<string> DownloadAsync(string modelUrl, CancellationToken cancellationToken = default);

    /// <summary>清理未成功关联到作品的本次模型文件。</summary>
    void Delete(string fileName);
}
