using Tongyin.Application.Ai;
using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>测试内聚的混元 3D 业务全链路，不访问真实收费服务。</summary>
public sealed class ModelGenerationServiceTests
{
    /// <summary>正常队列只提交一次，使用本地图片并保存混元 JobId 和配置快照。</summary>
    [Fact]
    public async Task Queue_ClaimSubmit_PersistsJobIdBeforePolling()
    {
        var t = new Scenario();
        await t.Service.ProcessNextAsync();
        Assert.Equal(1, t.Claims.Count);
        Assert.Equal(1, t.Client.SubmitCalls);
        Assert.Equal("hunyuan-job-1", t.Jobs.Current?.ProviderJobId);
        Assert.Equal("secret-v1", t.Jobs.Current?.ApiKeyCipher);
        Assert.Equal("3.0", t.Jobs.Current?.Model);
        Assert.Equal("cartoon.png", t.Original.LastFile);
    }

    /// <summary>已提交 JobId 下次只查询状态，不会重复请求新任务或再次计费。</summary>
    [Fact]
    public async Task SubmittedTask_QueriesSameJobWithoutResubmission()
    {
        var t = new Scenario();
        await t.Service.ProcessNextAsync();
        t.Client.Query = new Hunyuan3DResult("WAIT", null, null);
        await t.Service.ProcessNextAsync();
        t.Client.Query = new Hunyuan3DResult("RUN", null, null);
        await t.Service.ProcessNextAsync();
        Assert.Equal(1, t.Client.SubmitCalls);
        Assert.Equal(2, t.Client.QueryCalls);
        Assert.Equal("RUN", t.Jobs.Status);
    }

    /// <summary>管理员关闭新任务派发后，已提交 JobId 仍可正常查询，不能遗失已付费任务。</summary>
    [Fact]
    public async Task SubmittedJob_IsPolledEvenWhenNewDispatchDisabled()
    {
        var t = new Scenario();
        await t.Service.ProcessNextAsync();
        t.Settings.Enabled = false;
        t.Client.Query = new Hunyuan3DResult("RUN", null, null);
        await t.Service.ProcessNextAsync();
        Assert.Equal(1, t.Client.QueryCalls);
    }

    /// <summary>混元完成后下载真正的 GLB，写回作品且不重新提交。</summary>
    [Fact]
    public async Task Done_SavesGlbToWork()
    {
        var t = new Scenario();
        await t.Service.ProcessNextAsync();
        t.Client.Query = new Hunyuan3DResult("DONE", "https://assets.example/model.glb", null);
        await t.Service.ProcessNextAsync();
        Assert.Equal("new-model.glb", t.Result.Filename);
        Assert.Equal("DONE", t.Jobs.Status);
        Assert.Equal(1, t.Client.SubmitCalls);
        Assert.Equal(1, t.Media.DownloadCalls);
    }

    /// <summary>网络提交失败可能已经计费，不能在下一轮认领同一个任务并重复提交。</summary>
    [Fact]
    public async Task UncertainSubmission_NeverRepeatsPaidSubmit()
    {
        var t = new Scenario();
        t.Client.FailSubmit = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.ProcessNextAsync());
        await t.Service.ProcessNextAsync();
        Assert.Equal(1, t.Client.SubmitCalls);
        Assert.Equal(1, t.Claims.Count);
        Assert.Null(t.Jobs.Current);
    }

    /// <summary>真实失败登记但保留已确认卡通图，不重复生成。</summary>
    [Fact]
    public async Task ProviderFailure_RecordsFailure()
    {
        var t = new Scenario();
        await t.Service.ProcessNextAsync();
        t.Client.Query = new Hunyuan3DResult("FAIL", null, "InvalidImage");
        await t.Service.ProcessNextAsync();
        Assert.Contains("InvalidImage", t.Result.Error);
        Assert.Equal("FAIL", t.Jobs.Status);
    }

    private sealed class Scenario
    {
        public Queue Queue { get; } = new();
        public Claims Claims { get; } = new();
        public Jobs Jobs { get; } = new();
        public Settings Settings { get; } = new();
        public Original Original { get; } = new();
        public Client Client { get; } = new();
        public Media Media { get; } = new();
        public Results Result { get; }

        public Scenario()
        {
            Result = new Results(Jobs);
        }

        public ModelGenerationService Service => new(Queue, Claims, Jobs, Settings, Original, Client, Media, Result);
    }

    private sealed class Queue : IQueuedModelGenerationReader
    {
        public Task<IReadOnlyList<QueuedModelGeneration>> ListAsync(int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<QueuedModelGeneration>>(
                [new QueuedModelGeneration { WorkId = "work1", Owner = "parent", GenerationId = "gen1" }]);
    }

    private sealed class Claims : IModelGenerationClaimRepository
    {
        public int Count { get; private set; }
        public Task<WorkSnapshot?> ClaimAsync(string workId, string owner, string generationId, string providerId, int promptVersion, DateTimeOffset started, CancellationToken ct = default)
        {
            Count++;
            return Task.FromResult<WorkSnapshot?>(Count > 1 ? null : new WorkSnapshot
            {
                Id = workId, GenerationId = generationId, Status = "model_pending",
                Selected = 0, Candidates = ["cartoon.png"]
            });
        }
    }

    private sealed class Jobs : IModelGenerationJobRepository
    {
        public ModelGenerationJob? Current { get; private set; }
        public string Status { get; private set; } = "submitting";
        public Task<ModelGenerationJob?> GetNextSubmittedAsync(CancellationToken ct = default)
            => Task.FromResult(Status is "WAIT" or "RUN" ? Current : null);
        public Task RecordSubmittedAsync(string generationId, string providerJobId, string apiKeyCipher, string model, CancellationToken ct = default)
        {
            Current = new ModelGenerationJob("work1", generationId, providerJobId, "p1", apiKeyCipher, model);
            Status = "WAIT";
            return Task.CompletedTask;
        }
        public Task UpdateStatusAsync(string generationId, string status, CancellationToken ct = default)
        {
            Status = status;
            return Task.CompletedTask;
        }
        public void Finish(string status) => Status = status;
    }

    private sealed class Settings : IAiGenerationSettingsReader
    {
        public bool Enabled { get; set; } = true;
        public Task<AiGenerationSettings> GetAsync(string stage, CancellationToken ct = default)
            => Task.FromResult(new AiGenerationSettings
            {
                Stage = stage, PromptTemplate = "用于生成 3D 白模", PromptVersion = 1,
                Provider = Enabled ? new AiProviderSettings
                {
                    Id = "p1", Stage = "model", Adapter = "hunyuan3d",
                    Endpoint = "https://api.ai3d.cloud.tencent.com/v1/ai3d/submit",
                    Model = "3.0", ApiKeyCipher = "secret-v1", Enabled = true, Default = true
                } : null
            });
    }

    private sealed class Original : IOriginalImageReader
    {
        public string? LastFile { get; private set; }
        public Task<byte[]> ReadImageAsync(string fileName, CancellationToken ct = default)
        {
            LastFile = fileName;
            return Task.FromResult(new byte[] { 137,80,78,71,13,10,26,10 });
        }
    }

    private sealed class Client : IHunyuan3DClient
    {
        public int SubmitCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public bool FailSubmit { get; set; }
        public Hunyuan3DResult Query { get; set; } = new("WAIT", null, null);
        public Task<string> SubmitAsync(AiGenerationSettings settings, ReadOnlyMemory<byte> image, CancellationToken ct = default)
        {
            SubmitCalls++;
            return FailSubmit ? Task.FromException<string>(new InvalidOperationException("network uncertain"))
                : Task.FromResult("hunyuan-job-1");
        }
        public Task<Hunyuan3DResult> QueryAsync(AiGenerationSettings settings, string jobId, CancellationToken ct = default)
        {
            QueryCalls++;
            Assert.Equal("hunyuan-job-1", jobId);
            Assert.Equal("secret-v1", settings.Provider?.ApiKeyCipher);
            return Task.FromResult(Query);
        }
    }

    private sealed class Media : IGeneratedModelStore
    {
        public int DownloadCalls { get; private set; }
        public Task<string> DownloadAsync(string url, CancellationToken ct = default)
        {
            DownloadCalls++;
            return Task.FromResult("new-model.glb");
        }
        public void Delete(string name) { }
    }

    private sealed class Results(Jobs jobs) : IModelGenerationResultRepository
    {
        public string? Filename { get; private set; }
        public string Error { get; private set; } = "";
        public Task<WorkSnapshot> CompleteModelGenerationAsync(string workId, string generationId, string filename, CancellationToken ct = default)
        {
            Filename = filename;
            jobs.Finish("DONE");
            return Task.FromResult(new WorkSnapshot { Id = workId, GenerationId = generationId, Status = "model_ready", Model = filename });
        }
        public Task<WorkSnapshot> FailModelGenerationAsync(string workId, string generationId, string error, CancellationToken ct = default)
        {
            Error = error;
            jobs.Finish("FAIL");
            return Task.FromResult(new WorkSnapshot { Id = workId, GenerationId = generationId, Status = "failed", Note = error });
        }
    }
}
