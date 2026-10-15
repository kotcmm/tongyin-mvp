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
        var useCase = new CreateWork(repository, new RecordingImageStore());
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
        var useCase = new CreateWork(repository, new RecordingImageStore());

        var result = await useCase.ExecuteAsync("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");

        Assert.Same(repository.SavedWork, result);
    }

    /// <summary>
    /// 持久化失败时，不得把作品创建报告为成功。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenSaveFails_PropagatesFailure()
    {
        var useCase = new CreateWork(new FailingWorkRepository(), new RecordingImageStore());

        await Assert.ThrowsAsync<IOException>(() => useCase.ExecuteAsync("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png"));
    }

    /// <summary>
    /// 创建作品的完整流程必须先保存原画，并将返回的文件名写入作品记录。
    /// </summary>
    [Fact]
    public async Task ExecuteFromImageAsync_SavesOriginalAndPersistsWork()
    {
        var repository = new RecordingWorkRepository();
        var storage = new RecordingImageStore();
        var useCase = new CreateWork(repository, storage);
        using var drawing = new MemoryStream([1, 2, 3, 4]);

        var work = await useCase.ExecuteFromImageAsync("openid-001", drawing, drawing.Length);

        Assert.Equal("drawing.png", work.Original);
        Assert.Equal("openid-001", repository.SavedWork?.Owner);
        Assert.Same(repository.SavedWork, work);
        Assert.NotEmpty(work.Id);
        Assert.Equal(4, storage.SavedLength);
        Assert.Null(storage.DeletedName);
    }

    /// <summary>
    /// 数据库写入失败后，创建作品流程必须删除此前已保存的原画。
    /// </summary>
    [Fact]
    public async Task ExecuteFromImageAsync_WhenDatabaseFails_DeletesOriginal()
    {
        var storage = new RecordingImageStore();
        var useCase = new CreateWork(new FailingWorkRepository(), storage);
        using var drawing = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<IOException>(() => useCase.ExecuteFromImageAsync("openid-001", drawing, drawing.Length));

        Assert.Equal("drawing.png", storage.DeletedName);
    }

    /// <summary>
    /// 文件保存失败时不能写入作品记录。
    /// </summary>
    [Fact]
    public async Task ExecuteFromImageAsync_WhenImageSaveFails_DoesNotPersist()
    {
        var repository = new RecordingWorkRepository();
        var storage = new RecordingImageStore { ThrowOnSave = true };
        var useCase = new CreateWork(repository, storage);
        using var drawing = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<InvalidDataException>(() => useCase.ExecuteFromImageAsync("openid-001", drawing, drawing.Length));

        Assert.Null(repository.SavedWork);
        Assert.Null(storage.DeletedName);
    }

    private sealed class RecordingImageStore : IOriginalImageStore
    {
        public bool ThrowOnSave { get; set; }
        public long SavedLength { get; private set; }
        public string? DeletedName { get; private set; }

        public Task<string> SaveImageAsync(Stream image, long length, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave) throw new InvalidDataException("图片格式错误");
            SavedLength = length;
            return Task.FromResult("drawing.png");
        }

        public void Delete(string fileName)
        {
            DeletedName = fileName;
        }
    }

    private sealed class RecordingWorkRepository : IWorkRepository
    {
        public Work? SavedWork { get; private set; }


        public Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("创建用例测试不读取作品详情");

        public Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("创建用例测试不读取作品列表");
        }
        public Task AddAsync(Work work, CancellationToken cancellationToken = default)
        {
            SavedWork = work;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingWorkRepository : IWorkRepository
    {

        public Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("创建用例测试不读取作品详情");

        public Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("创建用例测试不读取作品列表");
        }
        public Task AddAsync(Work work, CancellationToken cancellationToken = default)
        {
            throw new IOException("保存失败");
        }
    }
}
