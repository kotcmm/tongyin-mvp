using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Tongyin.Application.Ai;
using Tongyin.Infrastructure.Ai;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Ai;

/// <summary>验证腾讯混元专业版官方 HTTP 提交和 JobId 查询协议，所有请求在测试内模拟，不产生费用。</summary>
public sealed class Hunyuan3DClientTests
{
    private const string Endpoint = "https://api.ai3d.cloud.tencent.com/v1/ai3d/submit";
    private static readonly byte[] Image = [137,80,78,71,13,10,26,10,0,0,0,0];
    private static readonly AiCredentialProtector Protector =
        new(DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "tongyin-hunyuan-client-tests"))));

    /// <summary>图片以本地 Base64 发送，不包含 callback_url 或 PUBLIC_URL，不带旧 Worker Bearer 前缀。</summary>
    [Fact]
    public async Task SubmitAsync_LocalImage_SendsOfficialCompatibleBodyAndReturnsJobId()
    {
        using var handler = new FakeHttp((request, body) =>
        {
            Assert.Equal("https://api.ai3d.cloud.tencent.com/v1/ai3d/submit", request.RequestUri!.ToString());
            Assert.Equal("sk-unit-only", request.Headers.GetValues("Authorization").Single());
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.Equal("3.0", root.GetProperty("Model").GetString());
            Assert.Equal("Geometry", root.GetProperty("GenerateType").GetString());
            Assert.StartsWith("data:image/png;base64,", root.GetProperty("ImageUrl").GetProperty("Url").GetString());
            Assert.DoesNotContain("callback_url", body);
            Assert.DoesNotContain("source_url", body);
            return "{\"Response\":{\"JobId\":\"hunyuan-job-001\"}}";
        });
        using var http = new HttpClient(handler);
        var id = await new Hunyuan3DClient(http, Protector).SubmitAsync(Settings(), Image);
        Assert.Equal("hunyuan-job-001", id);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>轮询 WAIT/RUN/DONE，必须找到 GLB 而不是 OBJ 文件。</summary>
    [Theory]
    [InlineData("WAIT", null)]
    [InlineData("RUN", null)]
    [InlineData("DONE", "https://assets.example/model.glb")]
    public async Task QueryAsync_ParsesRealHunyuanStatus(string status, string? expectedUrl)
    {
        using var handler = new FakeHttp((request, body) =>
        {
            Assert.Equal("https://api.ai3d.cloud.tencent.com/v1/ai3d/query", request.RequestUri!.ToString());
            using var json = JsonDocument.Parse(body);
            Assert.Equal("hunyuan-job-001", json.RootElement.GetProperty("JobId").GetString());
            return "{\"Response\":{\"Status\":\"" + status + "\",\"ResultFile3Ds\":[{\"Type\":\"OBJ\",\"Url\":\"https://assets.example/model.obj\"},{\"Type\":\"GLB\",\"Url\":\"https://assets.example/model.glb\"}]} }";
        });
        using var http = new HttpClient(handler);
        var result = await new Hunyuan3DClient(http, Protector).QueryAsync(Settings(), "hunyuan-job-001");
        Assert.Equal(status, result.Status);
        Assert.Equal(expectedUrl, result.GlbUrl);
    }

    /// <summary>提交无有效 JobId 时必须失败，不得再次向服务商发送模型生成请求。</summary>
    [Fact]
    public async Task SubmitAsync_MissingJobId_RejectsWithoutRetry()
    {
        using var handler = new FakeHttp((_, _) => "{\"message\":\"no id\"}");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new Hunyuan3DClient(http, Protector).SubmitAsync(Settings(), Image));
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>发送前识别非法图片和大文件，避免无效请求计费。</summary>
    [Fact]
    public async Task SubmitAsync_InvalidImage_NeverCallsProvider()
    {
        using var handler = new FakeHttp((_, _) => throw new Exception("不得发出请求"));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new Hunyuan3DClient(http, Protector).SubmitAsync(Settings(), new byte[] { 1,2,3 }));
        Assert.Equal(0, handler.Calls);
    }

    /// <summary>官方接口失败结果不得把响应中的 API Key 泄漏到异常字符串。</summary>
    [Fact]
    public async Task QueryAsync_FailedJob_ReturnsSanitizedCode()
    {
        using var handler = new FakeHttp((_, _) => "{\"Status\":\"FAIL\",\"ErrorCode\":\"InvalidImage\"}");
        using var http = new HttpClient(handler);
        var result = await new Hunyuan3DClient(http, Protector).QueryAsync(Settings(), "job-x");
        Assert.Equal("FAIL", result.Status);
        Assert.Contains("InvalidImage", result.Error);
    }

    /// <summary>非写死的公网兼容接口地址与未来模型 ID 均可实际用于提交和查询。</summary>
    [Fact]
    public async Task CustomEndpointAndModel_UseConfiguredSubmitAndSiblingQuery()
    {
        var endpoint = "https://gateway.example/tenant/ai3d/submit";
        using var handler = new FakeHttp((request, body) =>
        {
            var isSubmit = request.RequestUri!.AbsolutePath.EndsWith("/submit", StringComparison.Ordinal);
            Assert.Equal(isSubmit
                ? endpoint : "https://gateway.example/tenant/ai3d/query",
                request.RequestUri.ToString());
            using var json = JsonDocument.Parse(body);
            if (isSubmit)
            {
                Assert.Equal("future-4.0", json.RootElement.GetProperty("Model").GetString());
                return "{\"Response\":{\"JobId\":\"custom-job\"}}";
            }
            return "{\"Response\":{\"Status\":\"WAIT\"}}";
        });
        using var http = new HttpClient(handler);
        var settings = Settings(endpoint, "future-4.0");
        var ai = new Hunyuan3DClient(http, Protector);
        Assert.Equal("custom-job", await ai.SubmitAsync(settings, Image));
        Assert.Equal("WAIT", (await ai.QueryAsync(settings, "custom-job")).Status);
        Assert.Equal(2, handler.Calls);
    }

    /// <summary>管理员配置的私网 IP 和非 HTTPS 地址必须在任何 HTTP 请求前失败。</summary>
    [Theory]
    [InlineData("http://gateway.example/v1/ai3d/submit")]
    [InlineData("https://127.0.0.1/v1/ai3d/submit")]
    [InlineData("https://192.168.1.10/v1/ai3d/submit")]
    [InlineData("https://gateway.example/v1/ai3d/query")]
    [InlineData("https://gateway.example/v1/ai3d/submit?token=secret")]
    public async Task UnsafeOrWrongEndpoint_DoesNotSendRequest(string endpoint)
    {
        using var handler = new FakeHttp((_, _) => throw new InvalidOperationException("must not send"));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new Hunyuan3DClient(http, Protector).SubmitAsync(Settings(endpoint), Image));
        Assert.Equal(0, handler.Calls);
    }

    private static AiGenerationSettings Settings(string endpoint = Endpoint, string model = "3.0") => new()
    {
        Stage = "model", PromptVersion = 1, PromptTemplate = "保留结构",
        Provider = new AiProviderSettings
        {
            Id = "hunyuan-provider", Stage = "model", Adapter = "hunyuan3d",
            Endpoint = endpoint, Model = model, Enabled = true, Default = true,
            ApiKeyCipher = Protector.Encrypt("sk-unit-only")
        }
    };

    private sealed class FakeHttp(Func<HttpRequestMessage,string,string> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
        {
            Calls++;
            var body = await message.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(message, body), Encoding.UTF8, "application/json")
            };
        }
    }
}
