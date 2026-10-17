using Tongyin.Application.Ai;
using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>验证模型队列仅在服务商可用、原子认领成功时派发一次 worker 请求。</summary>
public sealed class DispatchQueuedModelGenerationTests
{
    /// <summary>数据库未配置可用 worker 时，不认领模型任务、不调用生成服务。</summary>
    [Fact]
    public async Task Dispatch_NoModelProvider_DoesNotClaimOrSend()
    {
        var env = new Scenario(providerEnabled: false);
        await env.Run();

        Assert.Equal(0, env.Claim.Attempts);
        Assert.Equal(0, env.Client.Submissions);
    }

    /// <summary>存在未认领模型任务时，使用同一任务编号提交给 worker。</summary>
    [Fact]
    public async Task Dispatch_ReadyModelTask_ClaimsThenSubmitsOnce()
    {
        var env = new Scenario(providerEnabled: true);
        await env.Run();

        Assert.Equal(1, env.Claim.Attempts);
        Assert.Equal("job-1", env.Claim.LastGenerationId);
        Assert.Equal(1, env.Client.Submissions);
        Assert.Equal("job-1", env.Client.LastWork?.GenerationId);
    }

    /// <summary>另一个执行者已认领任务时，绝不能再次调用收费模型。</summary>
    [Fact]
    public async Task Dispatch_ClaimDenied_DoesNotSubmit()
    {
        var env = new Scenario(providerEnabled: true);
        env.Claim.AllowClaim = false;
        await env.Run();

        Assert.Equal(1, env.Claim.Attempts);
        Assert.Equal(0, env.Client.Submissions);
    }

    private sealed class Scenario
    {
        public RecordingClaim Claim { get; } = new();
        public RecordingWorker Client { get; } = new();
        private readonly FakeSettings _settings;

        public Scenario(bool providerEnabled)
        {
            _settings = new FakeSettings(providerEnabled);
        }

        public Task Run() => new DispatchQueuedModelGeneration(
            new OneQueuedModel(), Claim, _settings, Client).DispatchNextAsync();
    }

    private sealed class OneQueuedModel : IQueuedModelGenerationReader
    {
        public Task<IReadOnlyList<QueuedModelGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QueuedModelGeneration>>(
                [new QueuedModelGeneration { WorkId = "work-1", Owner = "owner-1", GenerationId = "job-1" }]);
    }

    private sealed class FakeSettings(bool enabled) : IAiGenerationSettingsReader
    {
        public Task<AiGenerationSettings> GetAsync(string stage, CancellationToken cancellationToken = default)
            => Task.FromResult(new AiGenerationSettings
            {
                Stage = stage,
                PromptTemplate = "请根据卡通图生成适合打印的 GLB 模型",
                PromptVersion = 3,
                Provider = enabled
                    ? new AiProviderSettings
                    {
                        Id = "model-worker-1", Stage = "model", Adapter = "worker",
                        Endpoint = "https://worker.example.com/api/generate",
                        Enabled = true, Default = true, ApiKeyCipher = "encrypted-key"
                    }
                    : null
            });
    }

    private sealed class RecordingClaim : IModelGenerationClaimRepository
    {
        public int Attempts { get; private set; }
        public string? LastGenerationId { get; private set; }
        public bool AllowClaim { get; set; } = true;

        public Task<WorkSnapshot?> ClaimAsync(string workId, string owner, string generationId, string providerId, int promptVersion, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
        {
            Attempts++;
            LastGenerationId = generationId;
            return Task.FromResult<WorkSnapshot?>(AllowClaim
                ? new WorkSnapshot
                {
                    Id = workId, GenerationId = generationId, Status = "model_pending",
                    Candidates = ["selected.png"], Selected = 0
                }
                : null);
        }
    }

    private sealed class RecordingWorker : IModelWorkerClient
    {
        public int Submissions { get; private set; }
        public WorkSnapshot? LastWork { get; private set; }

        public Task SubmitAsync(AiGenerationSettings settings, WorkSnapshot work, CancellationToken cancellationToken = default)
        {
            Submissions++;
            LastWork = work;
            return Task.CompletedTask;
        }
    }
}
