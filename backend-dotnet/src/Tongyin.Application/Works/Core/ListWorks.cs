namespace Tongyin.Application.Works;

/// <summary>
/// 获取当前微信用户的作品列表，不承担 HTTP 鉴权或媒体 URL 签名。
/// </summary>
public sealed class ListWorks
{
    private readonly IWorkRepository _repository;

    /// <summary>
    /// 使用作品持久化端口构建查询用例。
    /// </summary>
    public ListWorks(IWorkRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// 查询指定微信用户可见的作品列表。
    /// </summary>
    public Task<IReadOnlyList<WorkSnapshot>> ExecuteAsync(string owner, CancellationToken cancellationToken = default)
    {
        return _repository.ListByOwnerAsync(owner, cancellationToken);
    }
}
