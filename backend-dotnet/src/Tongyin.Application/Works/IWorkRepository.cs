using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

/// <summary>
/// 作品持久化端口，由基础设施层实现具体存储。
/// </summary>
public interface IWorkRepository
{
    /// <summary>
    /// 保存新创建的儿童画作品。
    /// </summary>
    Task AddAsync(Work work, CancellationToken cancellationToken = default);

    /// <summary>
    /// 仅查询指定微信用户自己的作品快照，避免跨账号泄露。
    /// </summary>
    Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken cancellationToken = default);

    /// <summary>
    /// 同时使用作品 ID 和所属微信用户标识查询详情，未找到或不属于本人时返回空值。
    /// </summary>
    Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken cancellationToken = default);
}
