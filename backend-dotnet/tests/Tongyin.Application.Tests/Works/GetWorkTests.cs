using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>
/// 验证作品详情查询只允许读取指定微信用户的作品。
/// </summary>
public sealed class GetWorkTests
{
    /// <summary>
    /// 应用用例将作品 ID 与微信用户标识同时传给仓储，不能只凭 ID 查询。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PassesWorkIdAndOwnerToRepository()
    {
        var repository = new RecordingRepository();
        var getWork = new GetWork(repository);

        var work = await getWork.ExecuteAsync("openid-001", "work-001");

        Assert.Equal("work-001", repository.RequestedId);
        Assert.Equal("openid-001", repository.RequestedOwner);
        Assert.Equal("work-001", work?.Id);
    }

    /// <summary>
    /// 指定用户没有该作品时，用例返回空值，由 HTTP 层统一转换为 404。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenNotOwned_ReturnsNull()
    {
        var getWork = new GetWork(new RecordingRepository());

        var work = await getWork.ExecuteAsync("openid-other", "work-001");

        Assert.Null(work);
    }

    private sealed class RecordingRepository : IWorkRepository
    {
        public string? RequestedId { get; private set; }
        public string? RequestedOwner { get; private set; }

        public Task<WorkSnapshot> CompleteImageGenerationAsync(string id, string generationId, string imageFileName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("本测试不处理生成结果回写");

        public Task<WorkSnapshot?> RegisterImageGenerationAsync(string id, string owner, string generationId, int freeImageAttempts, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("当前测试不登记图片生成任务");

        public Task AddAsync(Work work, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken cancellationToken = default)
        {
            RequestedId = id;
            RequestedOwner = owner;
            return Task.FromResult<WorkSnapshot?>(owner == "openid-001" && id == "work-001"
                ? new WorkSnapshot { Id = id, Original = "drawing.png", Status = "image_pending" }
                : null);
        }
    }
}
