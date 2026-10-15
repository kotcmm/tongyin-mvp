using System.Security.Cryptography;
using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

/// <summary>
/// 编排儿童画作品创建：保存原画、登记作品，并在持久化失败时回收原画。
/// </summary>
public sealed class CreateWork
{
    private readonly IWorkRepository _workRepository;
    private readonly IOriginalImageStore _imageStore;

    /// <summary>
    /// 使用作品仓储及原画存储端口初始化创建作品用例。
    /// </summary>
    public CreateWork(IWorkRepository workRepository, IOriginalImageStore imageStore)
    {
        _workRepository = workRepository;
        _imageStore = imageStore;
    }

    /// <summary>
    /// 上传原画并创建作品；保存作品失败时清理本次上传的文件。
    /// </summary>
    public async Task<Work> ExecuteFromImageAsync(string owner, Stream image, long length, CancellationToken cancellationToken = default)
    {
        var fileName = await _imageStore.SaveImageAsync(image, length, cancellationToken);
        try
        {
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            return await ExecuteAsync(id, owner, DateTimeOffset.UtcNow, fileName, cancellationToken);
        }
        catch
        {
            _imageStore.Delete(fileName);
            throw;
        }
    }

    /// <summary>
    /// 将已有原画登记为作品并持久化，供不涉及上传的持久化用例使用。
    /// </summary>
    public async Task<Work> ExecuteAsync(string id, string owner, DateTimeOffset createdAt, string original, CancellationToken cancellationToken = default)
    {
        var work = Work.Create(id, owner, createdAt, original);
        await _workRepository.AddAsync(work, cancellationToken);
        return work;
    }
}
