using Tongyin.Application.Ai;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>图片生成一个内聚应用服务涵盖请求登记、任务认领、模型执行和结果回写。</summary>
public sealed class ImageGenerationServiceTests
{
    /// <summary>创建任务仅登记数据库，交给轮询器异步执行；不会在 HTTP 请求中调用收费模型。</summary>
    [Fact]
    public async Task QueueAsync_Enabled_RegistersOwnedWorkWithoutCallingModel()
    {
        var f = new Fixture();
        var result = await f.Service.QueueAsync("openid-001", "work-001", 3);
        Assert.NotNull(result);
        Assert.Equal("work-001", f.Works.RegisterId);
        Assert.Equal("openid-001", f.Works.RegisterOwner);
        Assert.Equal(3, f.Works.FreeAttempts);
        Assert.True(Guid.TryParseExact(f.Works.RegisterGenerationId, "N", out _));
        Assert.Equal(0, f.Generator.Calls);
        Assert.Equal(0, f.Queue.Calls);
    }

    /// <summary>未配置服务商不登记新任务，API 可明确返回 503。</summary>
    [Fact]
    public async Task QueueAsync_Disabled_ThrowsBeforeConsumingAttempt()
    {
        var f = new Fixture();
        f.Settings.Enabled = false;
        await Assert.ThrowsAsync<ImageGenerationUnavailableException>(() =>
            f.Service.QueueAsync("openid-001", "work-001", 2));
        Assert.Null(f.Works.RegisterId);
        Assert.Equal(0, f.Generator.Calls);
    }

    /// <summary>作品不属于请求用户时，返回空并由 HTTP 层继续返回 404。</summary>
    [Fact]
    public async Task QueueAsync_OtherOwnersWork_ReturnsNull()
    {
        var f = new Fixture();
        var result = await f.Service.QueueAsync("another-owner", "work-001", 2);
        Assert.Null(result);
    }

    /// <summary>配置停用后连候选队列都不读取，更不会产生外部请求。</summary>
    [Fact]
    public async Task ProcessNextAsync_Disabled_SkipsQueue()
    {
        var f = new Fixture();
        f.Settings.Enabled = false;
        Assert.Null(await f.Service.ProcessNextAsync());
        Assert.Equal(0, f.Queue.Calls);
        Assert.Equal(0, f.Generator.Calls);
    }

    /// <summary>队列为空不认领作品，也不调用 Seedream。</summary>
    [Fact]
    public async Task ProcessNextAsync_NoJobs_DoesNothing()
    {
        var f = new Fixture();
        f.Queue.Empty = true;
        Assert.Null(await f.Service.ProcessNextAsync());
        Assert.Equal(0, f.Works.ClaimCalls);
        Assert.Equal(0, f.Generator.Calls);
    }

    /// <summary>一次认领、一次模型请求、一次文件下载和一次回写；执行配置只读取一次。</summary>
    [Fact]
    public async Task ProcessNextAsync_ValidWork_ExecutesCompleteFlow()
    {
        var f = new Fixture();
        var result = await f.Service.ProcessNextAsync();
        Assert.Equal("image_ready", result?.Status);
        Assert.Equal("generated.png", Assert.Single(result!.Candidates));
        Assert.Equal(1, f.Works.ClaimCalls);
        Assert.Equal(1, f.Generator.Calls);
        Assert.Same(f.Settings.LastRead, f.Generator.LastSettings);
        Assert.Equal(1, f.Settings.Calls);
        Assert.Equal("drawing.png", f.Original.LastName);
        Assert.Equal("https://images.example/ready.png", f.Output.LastUrl);
        Assert.Equal("job-current", f.Works.LastCompletedGeneration);
        Assert.Empty(f.Output.Deleted);
    }

    /// <summary>旧任务或已经认领的任务不能重复发起可能计费的 API 调用。</summary>
    [Fact]
    public async Task ProcessNextAsync_StaleClaim_NeverCallsPaidProvider()
    {
        var f = new Fixture();
        f.Works.RejectClaim = true;
        Assert.Null(await f.Service.ProcessNextAsync());
        Assert.Equal(0, f.Generator.Calls);
        Assert.Null(f.Original.LastName);
    }

    /// <summary>读取原画失败明确在模型调用前发生，可以标记失败供用户查看。</summary>
    [Fact]
    public async Task ProcessNextAsync_MissingOriginal_RecordsFailure()
    {
        var f = new Fixture();
        f.Original.Missing = true;
        var result = await f.Service.ProcessNextAsync();
        Assert.Equal("failed", result?.Status);
        Assert.Equal("无法读取儿童原画", result?.Note);
        Assert.Equal(0, f.Generator.Calls);
    }

    /// <summary>模型连接中断可能已计费；保留认领不允许重复执行。</summary>
    [Fact]
    public async Task ProcessNextAsync_AmbiguousApiFailure_RequiresManualRecovery()
    {
        var f = new Fixture();
        f.Generator.Error = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ProcessNextAsync());
        Assert.Null(await f.Service.ProcessNextAsync());
        Assert.Equal(1, f.Generator.Calls);
        Assert.Equal("image_pending", f.Works.Status);
        Assert.Equal(0, f.Works.FailCalls);
    }

    /// <summary>下载模型结果失败，也不能再向第三方请求生成。</summary>
    [Fact]
    public async Task ProcessNextAsync_DownloadFailure_KeepsClaim()
    {
        var f = new Fixture();
        f.Output.Error = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.ProcessNextAsync());
        Assert.Null(await f.Service.ProcessNextAsync());
        Assert.Equal(1, f.Generator.Calls);
        Assert.Equal("image_pending", f.Works.Status);
    }

    /// <summary>数据库回写失败清理刚下载的未关联媒体文件，防止存储孤儿。</summary>
    [Fact]
    public async Task ProcessNextAsync_CompleteFailure_DeletesDownloadedImage()
    {
        var f = new Fixture();
        f.Works.RejectComplete = true;
        await Assert.ThrowsAsync<IOException>(() => f.Service.ProcessNextAsync());
        Assert.Equal("generated.png", Assert.Single(f.Output.Deleted));
        Assert.Equal(1, f.Generator.Calls);
        Assert.Equal("image_pending", f.Works.Status);
    }

    private sealed class Fixture
    {
        public WorkStore Works { get; } = new();
        public Settings Settings { get; } = new();
        public QueueReader Queue { get; } = new();
        public OriginalReader Original { get; } = new();
        public ImageClient Generator { get; } = new();
        public OutputStore Output { get; } = new();

        public ImageGenerationService Service =>
            new(Works, Settings, Queue, Original, Generator, Output);
    }

    private sealed class Settings : IAiGenerationSettingsReader
    {
        public int Calls { get; private set; }
        public bool Enabled { get; set; } = true;
        public AiGenerationSettings? LastRead { get; private set; }

        public Task<AiGenerationSettings> GetAsync(string stage, CancellationToken ct = default)
        {
            Calls++;
            Assert.Equal("image", stage);
            LastRead = new AiGenerationSettings
            {
                Stage = stage, PromptTemplate = "生成儿童卡通形象", PromptVersion = 2,
                Provider = Enabled ? new AiProviderSettings
                {
                    Stage = stage, Adapter = "seedream", Default = true, Enabled = true,
                    Model = "seedream-v1",
                    Endpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations",
                    ApiKeyCipher = "test-cipher"
                } : null
            };
            return Task.FromResult(LastRead);
        }
    }

    private sealed class QueueReader : IQueuedImageGenerationReader
    {
        public bool Empty { get; set; }
        public int Calls { get; private set; }
        public Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken ct = default)
        {
            Calls++;
            Assert.Equal(1, limit);
            return Task.FromResult<IReadOnlyList<QueuedImageGeneration>>(Empty ? [] :
                [new QueuedImageGeneration { Owner = "openid-001", WorkId = "work-001", GenerationId = "job-current" }]);
        }
    }

    private sealed class OriginalReader : IOriginalImageReader
    {
        public bool Missing { get; set; }
        public string? LastName { get; private set; }
        public Task<byte[]> ReadImageAsync(string name, CancellationToken ct = default)
        {
            LastName = name;
            return Missing ? Task.FromException<byte[]>(new FileNotFoundException("原画不存在"))
                : Task.FromResult(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1 });
        }
    }

    private sealed class ImageClient : IImageGenerationClient
    {
        public bool Error { get; set; }
        public int Calls { get; private set; }
        public AiGenerationSettings? LastSettings { get; private set; }
        public Task<string> GenerateAsync(AiGenerationSettings settings, ReadOnlyMemory<byte> image, CancellationToken ct = default)
        {
            Calls++;
            LastSettings = settings;
            return Error ? Task.FromException<string>(new InvalidOperationException("上游请求不确定"))
                : Task.FromResult("https://images.example/ready.png");
        }
    }

    private sealed class OutputStore : IGeneratedImageStore
    {
        public bool Error { get; set; }
        public string? LastUrl { get; private set; }
        public List<string> Deleted { get; } = [];
        public Task<string> DownloadAsync(string url, CancellationToken ct = default)
        {
            LastUrl = url;
            return Error ? Task.FromException<string>(new InvalidDataException("下载失败"))
                : Task.FromResult("generated.png");
        }
        public void Delete(string name) => Deleted.Add(name);
    }

    private sealed class WorkStore : IWorkRepository
    {
        private bool _claimed;
        public bool RejectClaim { get; set; }
        public bool RejectComplete { get; set; }
        public string Status { get; private set; } = "image_pending";
        public string? RegisterId { get; private set; }
        public string? RegisterOwner { get; private set; }
        public string? RegisterGenerationId { get; private set; }
        public int FreeAttempts { get; private set; }
        public int ClaimCalls { get; private set; }
        public int FailCalls { get; private set; }
        public string? LastCompletedGeneration { get; private set; }

        public Task<WorkSnapshot?> RegisterImageGenerationAsync(
            string id, string owner, string generationId, int freeAttempts, CancellationToken ct = default)
        {
            RegisterId = id;
            RegisterOwner = owner;
            RegisterGenerationId = generationId;
            FreeAttempts = freeAttempts;
            return Task.FromResult<WorkSnapshot?>(owner == "openid-001"
                ? new WorkSnapshot { Id = id, Status = "image_pending", GenerationId = generationId }
                : null);
        }

        public Task<WorkSnapshot?> ClaimImageGenerationAsync(
            string id, string owner, string generationId, DateTimeOffset startedAt, CancellationToken ct = default)
        {
            ClaimCalls++;
            if (RejectClaim || _claimed || id != "work-001" || owner != "openid-001" || generationId != "job-current")
                return Task.FromResult<WorkSnapshot?>(null);
            _claimed = true;
            return Task.FromResult<WorkSnapshot?>(new WorkSnapshot
            {
                Id = id, Original = "drawing.png", GenerationId = generationId, Status = Status
            });
        }

        public Task<WorkSnapshot> CompleteImageGenerationAsync(string id, string generationId, string file,
            CancellationToken ct = default)
        {
            if (RejectComplete) throw new IOException("数据库回写失败");
            Status = "image_ready";
            LastCompletedGeneration = generationId;
            return Task.FromResult(new WorkSnapshot
            {
                Id = id, GenerationId = generationId, Status = Status, Candidates = [file]
            });
        }

        public Task<WorkSnapshot> FailImageGenerationAsync(string id, string generationId, string reason,
            CancellationToken ct = default)
        {
            FailCalls++;
            Status = "failed";
            return Task.FromResult(new WorkSnapshot { Id = id, GenerationId = generationId, Status = Status, Note = reason });
        }

        public Task AddAsync(Work work, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
