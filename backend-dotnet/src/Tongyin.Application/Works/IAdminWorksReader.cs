namespace Tongyin.Application.Works;

/// <summary>运营管理端查询全体作品的只读端口，不复用微信用户的归属查询入口。</summary>
public interface IAdminWorksReader
{
    /// <summary>按创建时间倒序返回全部作品快照，具体媒体签名由 API 层完成。</summary>
    Task<IReadOnlyList<WorkSnapshot>> ListAsync(CancellationToken cancellationToken = default);
}
