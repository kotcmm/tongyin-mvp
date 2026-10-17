using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Api.Tests.Ai;

/// <summary>验证旧 Go worker 回调格式与专用认证、模型任务版本检查、文件清理规则。</summary>
public sealed class ModelResultEndpointTests
{
    private const string Route = "/api/internal/ai-result";

    /// <summary>没有 worker 凭据不能触碰下载器或模型记录。</summary>
    [Fact]
    public async Task Callback_MissingToken_ReturnsUnauthorized()
    {
        using var app = new ModelResultFactory();
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync(Route, ValidSuccess());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, app.Store.Downloads);
        Assert.Equal(0, app.Repository.Writes);
    }

    /// <summary>错误密钥不能使用管理员或微信会话绕过专用认证。</summary>
    [Fact]
    public async Task Callback_WrongToken_ReturnsUnauthorized()
    {
        using var app = new ModelResultFactory();
        using var client = app.CreateClient();
        using var request = NewRequest(ValidSuccess(), "wrong-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, app.Store.Downloads);
    }

    /// <summary>有效 3D 结果仅下载一次并写入当前模型任务，返回兼容 Go 的应答。</summary>
    [Fact]
    public async Task Callback_ValidModel_SavesAndCompletes()
    {
        using var app = new ModelResultFactory();
        using var client = app.CreateClient();
        using var request = NewRequest(ValidSuccess(), "test-worker-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("ok", result!["status"]);
        Assert.Equal("model_ready", app.Repository.Status);
        Assert.Equal("local-model.glb", app.Repository.ModelFile);
        Assert.Equal(1, app.Store.Downloads);
        Assert.Equal(0, app.Store.Deletes);
    }

    /// <summary>旧任务/重复结果不得替换当前作品，并应删除刚下载的孤儿文件。</summary>
    [Theory]
    [InlineData("stale-job")]
    [InlineData("wrong-id")]
    public async Task Callback_InvalidGeneration_CleansDownloadedFile(string generationId)
    {
        using var app = new ModelResultFactory();
        using var client = app.CreateClient();
        using var request = NewRequest(new
        {
            work_id = generationId == "wrong-id" ? "missing-work" : "work-1",
            stage = "model",
            generation_id = generationId == "wrong-id" ? "job-1" : generationId,
            model_url = "https://assets.example.com/model.glb"
        }, "test-worker-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("model_pending", app.Repository.Status);
        Assert.Equal(1, app.Store.Downloads);
        Assert.Equal(1, app.Store.Deletes);
    }

    /// <summary>失败回调仅更新当前模型任务的失败状态，不触发任何文件下载。</summary>
    [Fact]
    public async Task Callback_ModelFailed_RecordsFailureWithoutDownload()
    {
        using var app = new ModelResultFactory();
        using var client = app.CreateClient();
        using var request = NewRequest(new
        {
            work_id = "work-1", stage = "model", generation_id = "job-1", error = "3D 服务失败"
        }, "test-worker-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("failed", app.Repository.Status);
        Assert.Equal("3D 服务失败", app.Repository.Error);
        Assert.Equal(0, app.Store.Downloads);
    }

    /// <summary>不支持的阶段、缺少任务编号和缺少模型地址均在下载前拒绝。</summary>
    [Theory]
    [InlineData("image", "job-1", "https://assets.example.com/model.glb")]
    [InlineData("model", "", "https://assets.example.com/model.glb")]
    [InlineData("model", "job-1", "")]
    public async Task Callback_InvalidPayload_ReturnsBadRequest(string stage, string generationId, string modelUrl)
    {
        using var app = new ModelResultFactory();
        using var client = app.CreateClient();
        using var request = NewRequest(new
        {
            work_id = "work-1", stage, generation_id = generationId, model_url = modelUrl
        }, "test-worker-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.Store.Downloads);
    }

    private static object ValidSuccess() => new
    {
        work_id = "work-1", stage = "model", generation_id = "job-1",
        model_url = "https://assets.example.com/model.glb"
    };

    private static HttpRequestMessage NewRequest(object data, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(data) };
        request.Headers.TryAddWithoutValidation("X-AI-Token", token);
        return request;
    }

    private sealed class ModelResultFactory : WebApplicationFactory<Program>
    {
        public RecordingModelResultRepository Repository { get; } = new();
        public RecordingGeneratedModelStore Store { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "model-test-session-key-with-adequate-entropy",
                ["DB_CONNECTION_STRING"] = "unused-test",
                ["DB_DRIVER"] = "postgres",
                ["AI_WEBHOOK_TOKEN"] = "test-worker-token",
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IModelGenerationResultRepository>();
                services.AddSingleton<IModelGenerationResultRepository>(Repository);
                services.RemoveAll<IGeneratedModelStore>();
                services.AddSingleton<IGeneratedModelStore>(Store);
                services.RemoveAll<IQueuedImageGenerationReader>();
                services.AddSingleton<IQueuedImageGenerationReader, EmptyQueuedReader>();
            });
        }
    }

    private sealed class EmptyQueuedReader : IQueuedImageGenerationReader
    {
        public Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QueuedImageGeneration>>([]);
    }

    private sealed class RecordingGeneratedModelStore : IGeneratedModelStore
    {
        public int Downloads { get; private set; }
        public int Deletes { get; private set; }
        public Task<string> DownloadAsync(string modelUrl, CancellationToken cancellationToken = default)
        {
            Downloads++;
            return Task.FromResult("local-model.glb");
        }
        public void Delete(string fileName) => Deletes++;
    }

    private sealed class RecordingModelResultRepository : IModelGenerationResultRepository
    {
        public int Writes { get; private set; }
        public string Status { get; private set; } = "model_pending";
        public string? ModelFile { get; private set; }
        public string? Error { get; private set; }

        public Task<WorkSnapshot> CompleteModelGenerationAsync(string workId, string generationId, string modelFileName, CancellationToken cancellationToken = default)
        {
            Writes++;
            Validate(workId, generationId);
            Status = "model_ready";
            ModelFile = modelFileName;
            return Task.FromResult(new WorkSnapshot { Id = workId, Status = Status });
        }

        public Task<WorkSnapshot> FailModelGenerationAsync(string workId, string generationId, string error, CancellationToken cancellationToken = default)
        {
            Writes++;
            Validate(workId, generationId);
            Status = "failed";
            Error = error;
            return Task.FromResult(new WorkSnapshot { Id = workId, Status = Status });
        }

        private void Validate(string id, string generationId)
        {
            if (id != "work-1" || generationId != "job-1" || Status != "model_pending")
                throw new InvalidOperationException("任务已失效");
        }
    }
}
