namespace Tongyin.Application.Works;

/// <summary>
/// 作品原画存储端口：持久保存上传文件，并在作品创建失败时支持撤销。
/// </summary>
public interface IOriginalImageStore
{
    /// <summary>
    /// 保存上传的原画，返回可登记在作品上的存储文件名。
    /// </summary>
    Task<string> SaveImageAsync(Stream image, long length, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除尚未成功登记到作品的原画文件。
    /// </summary>
    void Delete(string fileName);
}
