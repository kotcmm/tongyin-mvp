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
}
