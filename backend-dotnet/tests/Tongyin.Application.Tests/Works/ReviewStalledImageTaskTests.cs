using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>
/// 人工核查应用用例必须验证依据并回收没有成功关联的媒体文件。
/// </summary>
public sealed class ReviewStalledImageTaskTests
{
    /// <summary>管理员确认失败时不能上传图片，审计所需的信息传给单个数据库事务。</summary>
    [Fact]
    public async Task ConfirmFailureAsync_ValidReason_ForwardsAuditFields()
    {
        var repo = new RecordingReviewRepository();
        var store = new RecordingImageStore();
        var review = new ReviewStalledImageTask(repo, store);

        var work = await review.ConfirmFailureAsync("work-1", "job-1", "legacy-token", "服务商确认请求失败");

        Assert.Equal("failed", work.Status);
        Assert.Equal("work-1", repo.WorkId);
        Assert.Equal("job-1", repo.GenerationId);
        Assert.Equal("legacy-token", repo.Actor);
        Assert.Equal("服务商确认请求失败", repo.Reason);
        Assert.Null(repo.FileName);
        Assert.Equal(0, store.SaveCalls);
    }

    /// <summary>空处理依据应在访问数据库或存储前拒绝。</summary>
    [Fact]
    public async Task ConfirmFailureAsync_MissingReason_Rejects()
    {
        var repo = new RecordingReviewRepository();
        var store = new RecordingImageStore();
        var review = new ReviewStalledImageTask(repo, store);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            review.ConfirmFailureAsync("work-1", "job-1", "legacy-token", "  "));

        Assert.Equal(0, repo.Calls);
        Assert.Equal(0, store.SaveCalls);
    }

    /// <summary>补录成功时只写入生成的本地文件名，不重复调用模型。</summary>
    [Fact]
    public async Task CompleteFromUploadAsync_ValidImage_PersistsLocalName()
    {
        var repo = new RecordingReviewRepository();
        var store = new RecordingImageStore();
        var review = new ReviewStalledImageTask(repo, store);
        await using var image = new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4]);

        var result = await review.CompleteFromUploadAsync("work-1", "job-1", "legacy-token", "核对成功", image, image.Length);

        Assert.Equal("image_ready", result.Status);
        Assert.Equal("recovered.png", repo.FileName);
        Assert.Equal(1, store.SaveCalls);
        Assert.Empty(store.Deleted);
    }

    /// <summary>事务发现旧任务时回收新上传文件，不能留无主媒体。</summary>
    [Fact]
    public async Task CompleteFromUploadAsync_StaleTask_DeletesUnreferencedImage()
    {
        var repo = new RecordingReviewRepository { Reject = true };
        var store = new RecordingImageStore();
        var review = new ReviewStalledImageTask(repo, store);
        await using var image = new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            review.CompleteFromUploadAsync("work-1", "job-old", "legacy-token", "核对成功", image, image.Length));

        Assert.Equal("recovered.png", Assert.Single(store.Deleted));
    }

    /// <summary>无依据或无有效任务编号时，不能先将上传文件落盘。</summary>
    [Theory]
    [InlineData("", "说明")]
    [InlineData("job-1", " ")]
    public async Task CompleteFromUploadAsync_InvalidInput_DoesNotSave(string generationId, string reason)
    {
        var repo = new RecordingReviewRepository();
        var store = new RecordingImageStore();
        var review = new ReviewStalledImageTask(repo, store);
        await using var image = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            review.CompleteFromUploadAsync("work-1", generationId, "legacy-token", reason, image, image.Length));

        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(0, repo.Calls);
    }

    private sealed class RecordingImageStore : IOriginalImageStore
    {
        public int SaveCalls { get; private set; }
        public List<string> Deleted { get; } = [];

        public Task<string> SaveImageAsync(Stream image, long length, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult("recovered.png");
        }

        public void Delete(string fileName) => Deleted.Add(fileName);
    }

    private sealed class RecordingReviewRepository : IAdminImageReviewRepository
    {
        public string? WorkId { get; private set; }
        public string? GenerationId { get; private set; }
        public string? Actor { get; private set; }
        public string? Reason { get; private set; }
        public string? FileName { get; private set; }
        public bool Reject { get; init; }
        public int Calls { get; private set; }

        public Task<WorkSnapshot> ResolveAsync(string workId, string generationId, string actor, string reason, string? fileName, DateTimeOffset reviewedAt, CancellationToken cancellationToken = default)
        {
            Calls++;
            WorkId = workId; GenerationId = generationId; Actor = actor;
            Reason = reason; FileName = fileName;
            if (Reject) throw new InvalidOperationException("过期的生成任务");
            return Task.FromResult(new WorkSnapshot
            {
                Id = workId, GenerationId = generationId, Status = fileName is null ? "failed" : "image_ready",
                Candidates = fileName is null ? [] : [fileName]
            });
        }
    }
}
