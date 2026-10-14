using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

// Aggregate persistence contract. Never exposes Go JSON or a database snapshot.

/// <summary>
/// 作品仓储接口，定义应用层需要的原子持久化操作。
/// </summary>
public interface IWorkRepository
{
    /// <summary>
    /// 持久化新增作品。
    /// </summary>
    Task AddAsync(Work work,CancellationToken cancellationToken=default);
    /// <summary>
    /// 按照所属用户查找业务对象。
    /// </summary>
    Task<Work?> FindAsync(string id,string owner,CancellationToken cancellationToken=default);
    /// <summary>
    /// 查询当前用户的作品列表。
    /// </summary>
    Task<IReadOnlyList<Work>> ListAsync(string owner,CancellationToken cancellationToken=default);
    /// <summary>
    /// 在原子事务中加载并修改聚合。
    /// </summary>
    Task<Work> UpdateAsync(string id,string owner,Action<Work> mutation,CancellationToken cancellationToken=default);
}
