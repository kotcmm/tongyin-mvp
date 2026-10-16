using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>
/// 验证图片生成结果下载、数据库回写及过期任务文件清理。
/// </summary>
public sealed class SaveImageGenerationResultTests
{
    /// <summary>
    /// 图片下载后只向仓储提交对应任务和本地文件名，成功时不得删除图片。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ValidResult_PersistsFileNameWithoutDeleting()
    {
        var store = new RecordingImageStore();
        var repository = new RecordingRepository();
        var useCase = new SaveImageGenerationResult(repository, store);

        var saved = await useCase.ExecuteAsync("work-001", "job-001", "https://images.example/generated.png");

        Assert.Equal("work-001", repository.Id);
        Assert.Equal("job-001", repository.GenerationId);
        Assert.Equal("generated.png", repository.FileName);
        Assert.Equal("image_ready", saved.Status);
        Assert.Equal("generated.png", Assert.Single(saved.Candidates));
        Assert.Equal("https://images.example/generated.png", store.RequestedUrl);
        Assert.Empty(store.DeletedFiles);
    }

    /// <summary>
    /// 作品生成任务已过期时，必须删除已下载、但未引用的文件。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_StaleTask_DeletesDownloadedFile()
    {
        var store = new RecordingImageStore();
        var repository = new RecordingRepository { Reject = true };
        var useCase = new SaveImageGenerationResult(repository, store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync("work-001", "job-old", "https://images.example/generated.png"));

        Assert.Equal("generated.png", Assert.Single(store.DeletedFiles));
    }

    /// <summary>
    /// 数据库写入失败后也要回收本次下载图片，防止媒体目录积累孤儿文件。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DatabaseFailure_DeletesDownloadedFile()
    {
        var store = new RecordingImageStore();
        var repository = new RecordingRepository { DatabaseError = true };
        var useCase = new SaveImageGenerationResult(repository, store);

        await Assert.ThrowsAsync<IOException>(() =>
            useCase.ExecuteAsync("work-001", "job-001", "https://images.example/generated.png"));

        Assert.Equal("generated.png", Assert.Single(store.DeletedFiles));
    }

    /// <summary>
    /// 下载阶段发生错误，没有本地文件需要回收，亦不能调用数据库。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DownloadFailure_DoesNotWriteDatabase()
    {
        var store = new RecordingImageStore { DownloadError = true };
        var repository = new RecordingRepository();
        var useCase = new SaveImageGenerationResult(repository, store);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            useCase.ExecuteAsync("work-001", "job-001", "https://images.example/invalid"));

        Assert.Null(repository.Id);
        Assert.Empty(store.DeletedFiles);
    }

    private sealed class RecordingImageStore : IGeneratedImageStore
    {
        public bool DownloadError { get; init; }
        public string? RequestedUrl { get; private set; }
        public List<string> DeletedFiles { get; } = [];

        public Task<string> DownloadAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
            RequestedUrl = imageUrl;
            if (DownloadError)
                throw new InvalidDataException("无法保存无效图片");

            return Task.FromResult("generated.png");
        }

        public void Delete(string fileName) => DeletedFiles.Add(fileName);
    }

    private sealed class RecordingRepository : IWorkRepository
    {
        public bool Reject { get; init; }
        public bool DatabaseError { get; init; }
        public string? Id { get; private set; }
        public string? GenerationId { get; private set; }
        public string? FileName { get; private set; }

        public Task<WorkSnapshot> FailImageGenerationAsync(string id, string generationId, string failureReason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("本测试不回写图片生成失败");

        public Task<WorkSnapshot> CompleteImageGenerationAsync(string id, string generationId, string imageFileName, CancellationToken cancellationToken = default)
        {
            Id = id;
            GenerationId = generationId;
            FileName = imageFileName;
            if (Reject)
                throw new InvalidOperationException("生成任务已失效，无法保存形象图片");
            if (DatabaseError)
                throw new IOException("模拟数据库写入失败");

            return Task.FromResult(new WorkSnapshot
            {
                Id = id, GenerationId = generationId, Status = "image_ready", Candidates = [imageFileName]
            });
        }

        public Task AddAsync(Work work, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkSnapshot?> ClaimImageGenerationAsync(string id, string owner, string generationId, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("本测试不认领图片任务");

        public Task<WorkSnapshot?> RegisterImageGenerationAsync(string id, string owner, string generationId, int freeImageAttempts, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
