using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tongyin.Application.Ai;
using Tongyin.Infrastructure.Ai;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Ai;

/// <summary>
/// 使用模拟 HTTP 管道验证 Seedream 图片适配器的请求协议、安全边界与响应处理，不访问付费接口。
/// </summary>
public sealed class SeedreamImageGeneratorTests
{
    private const string SessionSecret = "local-test-session-secret-with-32bytes";
    private const string ApiKey = "sk-test-中文-AI-密钥";
    private const string Ciphertext = "AAECAwQFBgcICQoLFkRZbdrMirsVfEoRLZOOtxWFtmeIMysZKY0/SkPmYje9JP1rZcI8yw";
    private const string OfficialEndpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations";
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];

    /// <summary>
    /// 正确请求必须提供 Bearer、原画 Data URL、模型和提示词，并要求单张 2K 无水印输出。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_Png_SendsGoCompatibleBodyAndReturnsSingleUrl()
    {
        var handler = new StubHandler(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(OfficialEndpoint, request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(ApiKey, request.Headers.Authorization?.Parameter);
            Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var body = json.RootElement;
            Assert.Equal("seedream-test-v1", body.GetProperty("model").GetString());
            Assert.Equal("保留原画主体", body.GetProperty("prompt").GetString());
            Assert.Equal("2K", body.GetProperty("size").GetString());
            Assert.Equal("url", body.GetProperty("response_format").GetString());
            Assert.False(body.GetProperty("watermark").GetBoolean());
            Assert.False(body.TryGetProperty("sequential_image_generation", out _));
            var input = body.GetProperty("image").GetString()!;
            Assert.StartsWith("data:image/png;base64,", input);
            Assert.Equal(Png, Convert.FromBase64String(input["data:image/png;base64,".Length..]));
            Assert.DoesNotContain("localhost", input);
            return Json(HttpStatusCode.OK, """{"data":[{"url":"https://images.example/generated.png"}]}""");
        });
        using var http = new HttpClient(handler);
        var adapter = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));

        var url = await adapter.GenerateAsync(ImageSettings(), Png);

        Assert.Equal("https://images.example/generated.png", url);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// 无默认服务商时必须拒绝，不允许发往不明确的地址。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_MissingProvider_RejectsBeforeHttp()
    {
        var handler = new StubHandler((_, _) => throw new Exception("不应发送请求"));
        using var http = new HttpClient(handler);
        var adapter = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));
        var settings = new AiGenerationSettings { Stage = "image", PromptTemplate = "提示词", PromptVersion = 1 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GenerateAsync(settings, Png));
        Assert.Equal(0, handler.Calls);
    }

    /// <summary>
    /// 模型生成或非 Seedream 适配器不得误调用图片接口。
    /// </summary>
    [Theory]
    [InlineData("model", "seedream")]
    [InlineData("image", "worker")]
    public async Task GenerateAsync_WrongStageOrAdapter_RejectsBeforeHttp(string stage, string adapter)
    {
        var handler = new StubHandler((_, _) => throw new Exception("不应发送请求"));
        using var http = new HttpClient(handler);
        var generator = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync(ImageSettings(stage: stage, adapter: adapter), Png));

        Assert.Equal(0, handler.Calls);
    }

    /// <summary>
    /// 不可信配置不可将解密后的 API Key 发送到第三方域名或非官方路径。
    /// </summary>
    [Theory]
    [InlineData("https://evil.example/api/v3/images/generations")]
    [InlineData("http://ark.cn-beijing.volces.com/api/v3/images/generations")]
    [InlineData("https://ark.cn-beijing.volces.com/api/v3/images/generations?secret=1")]
    [InlineData("https://ark.cn-beijing.volces.com.evil.example/api/v3/images/generations")]
    public async Task GenerateAsync_UntrustedEndpoint_RejectsBeforeHttp(string endpoint)
    {
        var handler = new StubHandler((_, _) => throw new Exception("不应发送请求"));
        using var http = new HttpClient(handler);
        var generator = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync(ImageSettings(endpoint: endpoint), Png));
        Assert.Equal(0, handler.Calls);
    }

    /// <summary>
    /// 文件格式不是图片时，拒绝向计费接口发送内容。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_NotAnImage_RejectsBeforeHttp()
    {
        var handler = new StubHandler((_, _) => throw new Exception("不应发送请求"));
        using var http = new HttpClient(handler);
        var generator = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            generator.GenerateAsync(ImageSettings(), Encoding.UTF8.GetBytes("not a picture")));

        Assert.Equal(0, handler.Calls);
    }

    /// <summary>
    /// 原画超过 10 MiB 时禁止提交计费请求。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_OversizedImage_RejectsBeforeHttp()
    {
        var handler = new StubHandler((_, _) => throw new Exception("不应发送请求"));
        using var http = new HttpClient(handler);
        var generator = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            generator.GenerateAsync(ImageSettings(), new byte[(10 * 1024 * 1024) + 1]));
        Assert.Equal(0, handler.Calls);
    }

    /// <summary>
    /// 上游错误正文可能带 API Key 和用户图片链接；错误消息不能直接泄露原文。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ProviderError_RetainsSafeCodeWithoutSecrets()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest,
            """{"error":{"code":"InvalidParameter.Image","message":"key sk-test-中文-AI-密钥; https://private.example/media/x?signature=secret; data:image/png;base64,aGVsbG8="}}""")));
        using var http = new HttpClient(handler);
        var generator = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(ImageSettings(), Png));

        Assert.Contains("InvalidParameter.Image", error.Message);
        Assert.Contains("400", error.Message);
        Assert.DoesNotContain(ApiKey, error.ToString());
        Assert.DoesNotContain("private.example", error.ToString());
        Assert.DoesNotContain("signature=secret", error.ToString());
        Assert.DoesNotContain("aGVsbG8", error.ToString());
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// 响应必须恰好包含一张图片，不能把多图或空结果当作成功。
    /// </summary>
    [Theory]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":[{"url":"https://images.example/1.png"},{"url":"https://images.example/2.png"}]}""")]
    [InlineData("""{"data":[{"url":""}]}""")]
    public async Task GenerateAsync_InvalidImageCount_Rejects(string responseJson)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, responseJson)));
        using var http = new HttpClient(handler);
        var generator = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));

        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(ImageSettings(), Png));
    }

    /// <summary>
    /// 上游响应提供非 HTTPS 的图片链接时，不能将其交给后续下载器。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_InsecureResultUrl_Rejects()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"data":[{"url":"http://localhost/media/test.png"}]}""")));
        using var http = new HttpClient(handler);
        var generator = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));

        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(ImageSettings(), Png));
    }

    /// <summary>
    /// 上游响应格式错误时只返回通用消息，不转储请求体或服务商正文。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_InvalidResponseJson_RejectsWithoutEchoingBody()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "NOT JSON sk-test-中文-AI-密钥")));
        using var http = new HttpClient(handler);
        var generator = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(ImageSettings(), Png));
        Assert.DoesNotContain(ApiKey, error.ToString());
    }

    /// <summary>
    /// 发生重定向时不得将密钥继续发送到第二个站点。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_RedirectResponse_Rejects()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://evil.example/capture") }
        }));
        using var http = new HttpClient(handler);
        var generator = new SeedreamImageGenerator(http, new GoAiKeyDecryptor(SessionSecret));

        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(ImageSettings(), Png));
        Assert.Equal(1, handler.Calls);
    }

    private static AiGenerationSettings ImageSettings(string stage = "image", string adapter = "seedream", string endpoint = OfficialEndpoint)
        => new()
        {
            Stage = stage,
            PromptTemplate = "保留原画主体",
            PromptVersion = 3,
            Provider = new AiProviderSettings
            {
                Id = "provider-image",
                Stage = stage,
                Adapter = adapter,
                Endpoint = endpoint,
                Model = "seedream-test-v1",
                Enabled = true,
                Default = true,
                ApiKeyCipher = Ciphertext
            }
        };

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }
}
