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

    /// <summary>
    /// 在共享数据库事务锁保护下登记图片生成任务，原子更新次数和状态。
    /// 作品不存在或不属于当前用户时返回空值；业务规则拒绝时抛出领域异常。
    /// </summary>
    Task<WorkSnapshot?> RegisterImageGenerationAsync(string id, string owner, string generationId, int freeImageAttempts, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在数据库事务中校验当前任务编号与图片待生成状态，并保存单张结果；过期或重复回写时拒绝。
    /// </summary>
    Task<WorkSnapshot> CompleteImageGenerationAsync(string id, string generationId, string imageFileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 仅对当前待生成的图片任务登记明确失败；过期和重复失败结果必须拒绝。
    /// </summary>
    Task<WorkSnapshot> FailImageGenerationAsync(string id, string generationId, string failureReason, CancellationToken cancellationToken = default);
}
