using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tongyin.Application.Ai;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Ai;
using Tongyin.Infrastructure.Media;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Ai;

/// <summary>验证 worker 请求符合旧 Go 协议，密钥仅进授权头且失败后不会自动重试。</summary>
public sealed class ModelWorkerClientTests
{
    private const string Secret = "local-worker-test-session-secret";

    /// <summary>送往 worker 的正文包含任务版本、签名图片 URL、回调地址和提示词。</summary>
    [Fact]
    public async Task SubmitAsync_ValidWorker_SendsOneCompatibleRequest()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Accepted);
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        await client.SubmitAsync(CreateSettings("https://worker.example.com/task"), CreateWork());

        Assert.Equal(1, handler.Sends);
        Assert.Equal("https://worker.example.com/task", handler.Endpoint);
        Assert.Equal("Bearer worker-secret", handler.Authorization);
        using var payload = JsonDocument.Parse(handler.Body!);
        var json = payload.RootElement;
        Assert.Equal("model", json.GetProperty("stage").GetString());
        Assert.Equal("work-1", json.GetProperty("work_id").GetString());
        Assert.Equal("job-1", json.GetProperty("generation_id").GetString());
        Assert.Equal("https://api.example.com/api/internal/ai-result", json.GetProperty("callback_url").GetString());
        Assert.Equal("图生3D测试提示词", json.GetProperty("instructions").GetString());
        Assert.Equal(3, json.GetProperty("prompt_version").GetInt32());
        Assert.StartsWith("https://api.example.com/media/cartoon.png?expires=", json.GetProperty("source_url").GetString());
        Assert.DoesNotContain("worker-secret", handler.Body!);
    }

    /// <summary>不安全的服务地址必须在发送任何 HTTP 请求前拒绝。</summary>
    [Theory]
    [InlineData("http://worker.example.com/task")]
    [InlineData("https://127.0.0.1/task")]
    [InlineData("https://worker.example.com:8443/task")]
    public async Task SubmitAsync_UnsafeEndpoint_RejectsBeforeNetwork(string endpoint)
    {
        using var handler = new RecordingHandler(HttpStatusCode.Accepted);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateClient(http).SubmitAsync(CreateSettings(endpoint), CreateWork()));
        Assert.Equal(0, handler.Sends);
    }

    /// <summary>worker 拒绝任务时只发送一次，不触发额外调用或重复计费。</summary>
    [Fact]
    public async Task SubmitAsync_RejectedWorker_DoesNotRetry()
    {
        using var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateClient(http).SubmitAsync(CreateSettings("https://worker.example.com/task"), CreateWork()));
        Assert.Equal(1, handler.Sends);
    }

    private static ModelWorkerClient CreateClient(HttpClient http)
        => new(http, new GoAiKeyDecryptor(Secret),
            new LocalMediaStore(Path.GetTempPath(), "https://api.example.com", Secret),
            "https://api.example.com");

    private static WorkSnapshot CreateWork() => new()
    {
        Id = "work-1", GenerationId = "job-1", Status = "model_pending",
        Selected = 0, Candidates = ["cartoon.png"]
    };

    private static AiGenerationSettings CreateSettings(string endpoint) => new()
    {
        Stage = "model", PromptTemplate = "图生3D测试提示词", PromptVersion = 3,
        Provider = new AiProviderSettings
        {
            Id = "model-1", Stage = "model", Adapter = "worker", Endpoint = endpoint,
            Model = "test-3d", Enabled = true, Default = true,
            ApiKeyCipher = Encrypt("worker-secret")
        }
    };

    private static string Encrypt(string plaintext)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("tongyin-ai-api-key-v1:" + Secret));
        var nonce = new byte[12];
        var clear = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = new byte[clear.Length];
        var tag = new byte[16];
        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, clear, encrypted, tag);
        return Convert.ToBase64String([.. nonce, .. encrypted, .. tag]).TrimEnd('=');
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Sends { get; private set; }
        public string? Endpoint { get; private set; }
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sends++;
            Endpoint = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }
}
