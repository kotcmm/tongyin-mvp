namespace Tongyin.Application.Works;

/// <summary>
/// 查询单个微信用户作品的详情，访问权限以仓储的作品 ID 与所有者联合筛选为准。
/// </summary>
public sealed class GetWork(IWorkRepository repository)
{
    /// <summary>
    /// 获取当前用户拥有的指定作品，不存在或无访问权均返回空值。
    /// </summary>
    public Task<WorkSnapshot?> ExecuteAsync(string owner, string id, CancellationToken cancellationToken = default)
    {
        return repository.GetByIdForOwnerAsync(id, owner, cancellationToken);
    }
}
