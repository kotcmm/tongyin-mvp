using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>
/// 发起图片生成任务时只登记持久化状态，不在应用用例中模拟 AI 执行。
/// </summary>
public sealed class StartImageGenerationTests
{
    /// <summary>
    /// 应用用例生成唯一任务编号，并把用户、作品和额度传给仓储完成原子登记。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RegistersOwnedWorkWithGenerationId()
    {
        var repository = new RecordingRepository();
        var useCase = new StartImageGeneration(repository);

        var saved = await useCase.ExecuteAsync("openid-001", "work-001", 3);

        Assert.NotNull(saved);
        Assert.Equal("openid-001", repository.Owner);
        Assert.Equal("work-001", repository.Id);
        Assert.Equal(3, repository.FreeAttempts);
        Assert.True(Guid.TryParseExact(repository.GenerationId, "N", out _));
        Assert.Equal(repository.GenerationId, saved.GenerationId);
        Assert.Equal(1, saved.ImageAttempts);
    }

    /// <summary>
    /// 不属于当前用户的作品不能生成有效登记记录。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_UnknownOrForeignWork_ReturnsNull()
    {
        var useCase = new StartImageGeneration(new RecordingRepository());

        var saved = await useCase.ExecuteAsync("openid-other", "work-001");

        Assert.Null(saved);
    }

    private sealed class RecordingRepository : IWorkRepository
    {
        public string? Id { get; private set; }
        public string? Owner { get; private set; }
        public string? GenerationId { get; private set; }
        public int FreeAttempts { get; private set; }

        public Task AddAsync(Work work, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkSnapshot> CompleteImageGenerationAsync(string id, string generationId, string imageFileName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("本测试不处理生成结果回写");

        public Task<WorkSnapshot?> RegisterImageGenerationAsync(string id, string owner, string generationId, int freeImageAttempts, CancellationToken cancellationToken = default)
        {
            Id = id;
            Owner = owner;
            GenerationId = generationId;
            FreeAttempts = freeImageAttempts;
            return Task.FromResult<WorkSnapshot?>(owner == "openid-001"
                ? new WorkSnapshot { Id = id, Status = "image_pending", ImageAttempts = 1, GenerationId = generationId }
                : null);
        }
    }
}
