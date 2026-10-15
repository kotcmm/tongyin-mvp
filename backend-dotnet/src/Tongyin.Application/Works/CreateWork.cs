using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

/// <summary>
/// 创建儿童画作品的应用用例，将已上传原画登记并持久化。
/// </summary>
public sealed class CreateWork
{
    private readonly IWorkRepository _workRepository;

    /// <summary>
    /// 使用真实作品存储端口初始化创建作品用例。
    /// </summary>
    public CreateWork(IWorkRepository workRepository)
    {
        _workRepository = workRepository;
    }

    /// <summary>
    /// 创建并保存作品；仅当保存成功后返回新作品。
    /// </summary>
    public async Task<Work> ExecuteAsync(string id, string owner, DateTimeOffset createdAt, string original, CancellationToken cancellationToken = default)
    {
        var work = Work.Create(id, owner, createdAt, original);
        await _workRepository.AddAsync(work, cancellationToken);
        return work;
    }
}
