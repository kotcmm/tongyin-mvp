using Tongyin.Application.Ai;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>
/// 调度一次现有已登记任务必须复用数据库认领与执行用例，不在调度器中调用外部模型。
/// </summary>
public sealed class DispatchQueuedImageGenerationTests
{
    /// <summary>
    /// 读取到待执行任务时使用数据库归属、作品编号和任务编号执行现有流程。
    /// </summary>
    [Fact]
    public async Task DispatchNextAsync_QueuedImage_ExecutesAndCompletes()
    {
        var repository = new RecordingRepository();
        var generator = new RecordingGenerator();
        var queued = new RecordingQueuedReader();
        var dispatcher = CreateDispatcher(repository, generator, queued);

        await dispatcher.DispatchNextAsync();

        Assert.Equal(1, queued.Calls);
        Assert.Equal(1, repository.ClaimCalls);
        Assert.Equal(1, generator.Calls);
        Assert.Equal("image_ready", repository.Status);
        Assert.Equal("job-current", repository.LastGenerationId);
        Assert.Equal("openid-001", repository.LastOwner);
    }

    /// <summary>
    /// 未发现已登记任务时，不认领、不调用模型，也不消耗任何图片生成次数。
    /// </summary>
    [Fact]
    public async Task DispatchNextAsync_NoPendingJob_DoesNothing()
    {
        var repository = new RecordingRepository();
        var generator = new RecordingGenerator();
        var queued = new RecordingQueuedReader { Empty = true };
        var dispatcher = CreateDispatcher(repository, generator, queued);

        await dispatcher.DispatchNextAsync();

        Assert.Equal(0, repository.ClaimCalls);
        Assert.Equal(0, generator.Calls);
    }

    /// <summary>
    /// 即使候选列表缓存了同一任务，第二次投递因认领已存在不得重复调用付费模型。
    /// </summary>
    [Fact]
    public async Task DispatchNextAsync_RedeliveryAfterUncertainFailure_NeverRegenerates()
    {
        var repository = new RecordingRepository();
        var generator = new RecordingGenerator { FailAfterSubmit = true };
        var dispatcher = CreateDispatcher(repository, generator, new RecordingQueuedReader());

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchNextAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchNextAsync());

        Assert.Equal(1, generator.Calls);
        Assert.Equal(2, repository.ClaimCalls);
        Assert.Equal("image_pending", repository.Status);
    }

    private static DispatchQueuedImageGeneration CreateDispatcher(RecordingRepository repository, RecordingGenerator generator, RecordingQueuedReader reader)
    {
        var save = new SaveImageGenerationResult(repository, new RecordingGeneratedStore());
        var executor = new ExecuteImageGeneration(repository, new RecordingSettings(), new RecordingOriginal(), generator, save);
        return new DispatchQueuedImageGeneration(reader, executor);
    }

    private sealed class RecordingQueuedReader : IQueuedImageGenerationReader
    {
        public bool Empty { get; init; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(1, limit);
            return Task.FromResult<IReadOnlyList<QueuedImageGeneration>>(Empty ? [] :
            [new QueuedImageGeneration { Owner = "openid-001", WorkId = "work-001", GenerationId = "job-current" }]);
        }
    }

    private sealed class RecordingSettings : IAiGenerationSettingsReader
    {
        public Task<AiGenerationSettings> GetAsync(string stage, CancellationToken cancellationToken = default)
            => Task.FromResult(new AiGenerationSettings
            {
                Stage = "image", PromptTemplate = "绘制卡通形象", Provider = new AiProviderSettings
                {
                    Stage = "image", Adapter = "seedream", Enabled = true, Default = true,
                    Model = "seedream-test", Endpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations"
                }
            });
    }

    private sealed class RecordingOriginal : IOriginalImageReader
    {
        public Task<byte[]> ReadImageAsync(string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0 });
    }

    private sealed class RecordingGenerator : IImageGenerationClient
    {
        public bool FailAfterSubmit { get; init; }
        public int Calls { get; private set; }

        public Task<string> GenerateAsync(AiGenerationSettings settings, ReadOnlyMemory<byte> originalImage, CancellationToken cancellationToken = default)
        {
            Calls++;
            return FailAfterSubmit
                ? Task.FromException<string>(new InvalidOperationException("上游结果不确定"))
                : Task.FromResult("https://images.example/ready.png");
        }
    }

    private sealed class RecordingGeneratedStore : IGeneratedImageStore
    {
        public Task<string> DownloadAsync(string imageUrl, CancellationToken cancellationToken = default)
            => Task.FromResult("ready.png");

        public void Delete(string fileName) { }
    }

    private sealed class RecordingRepository : IWorkRepository
    {
        private bool _claimed;
        public string Status { get; private set; } = "image_pending";
        public int ClaimCalls { get; private set; }
        public string? LastGenerationId { get; private set; }
        public string? LastOwner { get; private set; }

        public Task<WorkSnapshot?> ClaimImageGenerationAsync(string id, string owner, string generationId, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
        {
            ClaimCalls++;
            LastOwner = owner;
            LastGenerationId = generationId;
            if (_claimed || id != "work-001" || owner != "openid-001" || generationId != "job-current")
                return Task.FromResult<WorkSnapshot?>(null);

            _claimed = true;
            return Task.FromResult<WorkSnapshot?>(new WorkSnapshot
            {
                Id = id, Original = "drawing.png", Status = Status, GenerationId = generationId
            });
        }

        public Task<WorkSnapshot> CompleteImageGenerationAsync(string id, string generationId, string imageFileName, CancellationToken cancellationToken = default)
        {
            Status = "image_ready";
            return Task.FromResult(new WorkSnapshot { Id = id, Status = Status, GenerationId = generationId });
        }

        public Task<WorkSnapshot> FailImageGenerationAsync(string id, string generationId, string failureReason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddAsync(Work work, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkSnapshot?> RegisterImageGenerationAsync(string id, string owner, string generationId, int freeImageAttempts, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
