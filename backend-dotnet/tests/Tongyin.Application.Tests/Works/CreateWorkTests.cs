using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>
/// 验证创建作品用例将已上传原画登记到作品存储中。
/// </summary>
public sealed class CreateWorkTests
{
    /// <summary>
    /// 创建作品时应将用户、原画、时间及初始状态保存到仓储。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PersistsWorkWithOriginalDrawing()
    {
        var repository = new RecordingWorkRepository();
        var useCase = new CreateWork(repository);
        var createdAt = new DateTimeOffset(2026, 10, 15, 10, 30, 0, TimeSpan.FromHours(8));

        await useCase.ExecuteAsync("work-001", "openid-001", createdAt, "media/drawing.png");

        Assert.NotNull(repository.SavedWork);
        Assert.Equal("work-001", repository.SavedWork.Id);
        Assert.Equal("openid-001", repository.SavedWork.Owner);
        Assert.Equal(createdAt, repository.SavedWork.CreatedAt);
        Assert.Equal("media/drawing.png", repository.SavedWork.Original);
        Assert.Equal(WorkStatus.ImagePending, repository.SavedWork.Status);
    }

    /// <summary>
    /// 创建成功后应将刚刚保存的作品返回给调用者。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ReturnsPersistedWork()
    {
        var repository = new RecordingWorkRepository();
        var useCase = new CreateWork(repository);

        var result = await useCase.ExecuteAsync("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");

        Assert.Same(repository.SavedWork, result);
    }

    /// <summary>
    /// 持久化失败时，不得把作品创建报告为成功。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenSaveFails_PropagatesFailure()
    {
        var useCase = new CreateWork(new FailingWorkRepository());

        await Assert.ThrowsAsync<IOException>(() => useCase.ExecuteAsync("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png"));
    }

    private sealed class RecordingWorkRepository : IWorkRepository
    {
        public Work? SavedWork { get; private set; }

        public Task AddAsync(Work work, CancellationToken cancellationToken = default)
        {
            SavedWork = work;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingWorkRepository : IWorkRepository
    {
        public Task AddAsync(Work work, CancellationToken cancellationToken = default)
        {
            throw new IOException("保存失败");
        }
    }
}
