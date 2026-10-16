using Tongyin.Application.Ai;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>
/// 图片生成执行用例只协调已登记的任务，不直接操作 HTTP、数据库事务或文件路径。
/// </summary>
public sealed class ExecuteImageGenerationTests
{
    /// <summary>
    /// 有效任务按顺序读取配置、读取原画、调用模型并保存唯一图片结果。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CurrentTask_GeneratesAndPersistsSingleImage()
    {
        var repository = new RecordingRepository();
        var reader = new RecordingOriginalReader();
        var generator = new RecordingGenerator();
        var output = new RecordingGeneratedStore();
        var useCase = CreateUseCase(repository, new RecordingSettingsReader(), reader, generator, output);

        var result = await useCase.ExecuteAsync("openid-001", "work-001", "job-current");

        Assert.Equal("image_ready", result.Status);
        Assert.Equal("generated.png", Assert.Single(result.Candidates));
        Assert.Equal("drawing.png", reader.LastFileName);
        Assert.Equal(1, generator.Calls);
        Assert.Equal("seedream-v1", generator.LastSettings?.Provider?.Model);
        Assert.Equal(reader.Image, generator.LastImage);
        Assert.Equal("https://images.example/result.png", output.LastUrl);
        Assert.Equal("job-current", repository.LastCompletedGeneration);
        Assert.Equal(0, repository.FailCount);
    }

    /// <summary>
    /// 过期任务不能读取原画或向模型发起可能计费的请求。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_StaleTask_RejectsBeforeExternalWork()
    {
        var repository = new RecordingRepository();
        var reader = new RecordingOriginalReader();
        var generator = new RecordingGenerator();
        var settings = new RecordingSettingsReader();
        var useCase = CreateUseCase(repository, settings, reader, generator);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync("openid-001", "work-001", "job-old"));

        Assert.Equal(0, settings.Calls);
        Assert.Null(reader.LastFileName);
        Assert.Equal(0, generator.Calls);
        Assert.Equal(0, repository.FailCount);
    }

    /// <summary>
    /// 无服务商或只有不支持的适配器时，明确标记失败且不调用计费接口。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("worker")]
    public async Task ExecuteAsync_NoSeedreamProvider_RecordsDefinitiveFailure(string? adapter)
    {
        var repository = new RecordingRepository();
        var generator = new RecordingGenerator();
        var reader = new RecordingOriginalReader();
        var settings = new RecordingSettingsReader
        {
            Settings = ConfiguredSettings(adapter)
        };
        var useCase = CreateUseCase(repository, settings, reader, generator);

        var result = await useCase.ExecuteAsync("openid-001", "work-001", "job-current");

        Assert.Equal("failed", result.Status);
        Assert.Equal("未配置可用的图片生成服务商", result.Note);
        Assert.Equal("job-current", repository.LastFailedGeneration);
        Assert.Equal(1, repository.FailCount);
        Assert.Equal(0, generator.Calls);
        Assert.Null(reader.LastFileName);
    }

    /// <summary>
    /// 原画文件丢失是在模型提交前已明确发生的错误，应原子标记失败。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_MissingOriginal_RecordsFailureWithoutModelCall()
    {
        var repository = new RecordingRepository();
        var generator = new RecordingGenerator();
        var reader = new RecordingOriginalReader { Missing = true };
        var useCase = CreateUseCase(repository, new RecordingSettingsReader(), reader, generator);

        var result = await useCase.ExecuteAsync("openid-001", "work-001", "job-current");

        Assert.Equal("failed", result.Status);
        Assert.Equal("无法读取儿童原画", result.Note);
        Assert.Equal(1, repository.FailCount);
        Assert.Equal(0, generator.Calls);
    }

    /// <summary>
    /// HTTP 超时可能已经计费，不应错误写 failed 后再次生成。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SeedreamTimeout_PreservesPendingForReconciliation()
    {
        var repository = new RecordingRepository();
        var generator = new RecordingGenerator { Timeout = true };
        var useCase = CreateUseCase(repository, new RecordingSettingsReader(), new RecordingOriginalReader(), generator);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync("openid-001", "work-001", "job-current"));

        Assert.Equal("image_pending", repository.Snapshot.Status);
        Assert.Equal(0, repository.FailCount);
        Assert.Null(repository.LastCompletedGeneration);
    }

    /// <summary>
    /// 已成功调用服务商后若图片下载失败，同样不能盲目重新调用模型。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DownloadFailure_PreservesPendingForReconciliation()
    {
        var repository = new RecordingRepository();
        var output = new RecordingGeneratedStore { Reject = true };
        var useCase = CreateUseCase(repository, new RecordingSettingsReader(), new RecordingOriginalReader(), new RecordingGenerator(), output);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            useCase.ExecuteAsync("openid-001", "work-001", "job-current"));

        Assert.Equal("image_pending", repository.Snapshot.Status);
        Assert.Equal(0, repository.FailCount);
        Assert.Null(repository.LastCompletedGeneration);
    }

    private static ExecuteImageGeneration CreateUseCase(RecordingRepository repository, RecordingSettingsReader settings, RecordingOriginalReader reader, RecordingGenerator generator, RecordingGeneratedStore? output = null)
    {
        return new ExecuteImageGeneration(
            repository, settings, reader, generator,
            new SaveImageGenerationResult(repository, output ?? new RecordingGeneratedStore()));
    }

    private static AiGenerationSettings ConfiguredSettings(string? adapter = "seedream")
    {
        return new AiGenerationSettings
        {
            Stage = "image",
            PromptTemplate = "保留儿童画主体",
            PromptVersion = 1,
            Provider = adapter is null ? null : new AiProviderSettings
            {
                Id = "provider-001", Stage = "image", Adapter = adapter,
                Endpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations",
                Model = "seedream-v1", Enabled = true, Default = true
            }
        };
    }

    private sealed class RecordingSettingsReader : IAiGenerationSettingsReader
    {
        public int Calls { get; private set; }
        public AiGenerationSettings Settings { get; init; } = ConfiguredSettings();

        public Task<AiGenerationSettings> GetAsync(string stage, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal("image", stage);
            return Task.FromResult(Settings);
        }
    }

    private sealed class RecordingOriginalReader : IOriginalImageReader
    {
        public byte[] Image { get; } = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
        public string? LastFileName { get; private set; }
        public bool Missing { get; init; }

        public Task<byte[]> ReadImageAsync(string fileName, CancellationToken cancellationToken = default)
        {
            LastFileName = fileName;
            return Missing
                ? Task.FromException<byte[]>(new FileNotFoundException("原画丢失"))
                : Task.FromResult(Image);
        }
    }

    private sealed class RecordingGenerator : IImageGenerationClient
    {
        public int Calls { get; private set; }
        public bool Timeout { get; init; }
        public AiGenerationSettings? LastSettings { get; private set; }
        public byte[]? LastImage { get; private set; }

        public Task<string> GenerateAsync(AiGenerationSettings settings, ReadOnlyMemory<byte> originalImage, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastSettings = settings;
            LastImage = originalImage.ToArray();
            return Timeout
                ? Task.FromException<string>(new InvalidOperationException("Seedream 请求超时，可能已计费"))
                : Task.FromResult("https://images.example/result.png");
        }
    }

    private sealed class RecordingGeneratedStore : IGeneratedImageStore
    {
        public bool Reject { get; init; }
        public string? LastUrl { get; private set; }

        public Task<string> DownloadAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
            LastUrl = imageUrl;
            return Reject
                ? Task.FromException<string>(new InvalidDataException("响应内容不是图片"))
                : Task.FromResult("generated.png");
        }

        public void Delete(string fileName) { }
    }

    private sealed class RecordingRepository : IWorkRepository
    {
        public WorkSnapshot Snapshot { get; private set; } = new()
        {
            Id = "work-001", Original = "drawing.png", Status = "image_pending", GenerationId = "job-current"
        };
        public int FailCount { get; private set; }
        public string? LastFailedGeneration { get; private set; }
        public string? LastCompletedGeneration { get; private set; }

        public Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkSnapshot?>(id == "work-001" && owner == "openid-001" ? Snapshot : null);

        public Task<WorkSnapshot> FailImageGenerationAsync(string id, string generationId, string failureReason, CancellationToken cancellationToken = default)
        {
            FailCount++;
            LastFailedGeneration = generationId;
            Snapshot = new WorkSnapshot { Id = id, GenerationId = generationId, Status = "failed", Note = failureReason };
            return Task.FromResult(Snapshot);
        }

        public Task<WorkSnapshot> CompleteImageGenerationAsync(string id, string generationId, string imageFileName, CancellationToken cancellationToken = default)
        {
            LastCompletedGeneration = generationId;
            Snapshot = new WorkSnapshot
            {
                Id = id, GenerationId = generationId, Status = "image_ready", Candidates = [imageFileName]
            };
            return Task.FromResult(Snapshot);
        }

        public Task AddAsync(Work work, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkSnapshot?> RegisterImageGenerationAsync(string id, string owner, string generationId, int freeImageAttempts, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
