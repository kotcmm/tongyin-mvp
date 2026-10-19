using System.Net.Http.Json;
using System.Text.Json;
using Tongyin.Application.Ai;

namespace Tongyin.Infrastructure.Ai;

/// <summary>
/// 腾讯混元生 3D 专业版 OpenAI 兼容接口；提交 ImageUrl Base64 并按 JobId 查询。
/// 不依赖第三方回调或本机公网地址，不自动重试收费提交。
/// </summary>
public sealed class Hunyuan3DClient(HttpClient client, AiCredentialProtector credentials) : IHunyuan3DClient
{
    private static readonly Uri SubmitUri = new("https://api.ai3d.cloud.tencent.com/v1/ai3d/submit");
    private static readonly Uri QueryUri = new("https://api.ai3d.cloud.tencent.com/v1/ai3d/query");

    /// <summary>向官方提交本地图片，使用原始 API Key Authorization 头而不是 Worker Bearer 协议。</summary>
    public async Task<string> SubmitAsync(
        AiGenerationSettings settings, ReadOnlyMemory<byte> image, CancellationToken ct = default)
    {
        var provider = VerifyProvider(settings);
        if (image.Length == 0 || image.Length > 6 * 1024 * 1024)
            throw new InvalidDataException("混元输入图片不可为空或超过 6 MiB");

        var mime = DetectMime(image.Span);
        if (mime is null) throw new InvalidDataException("混元只接受 JPG、PNG 或 WebP 卡通图片");

        // 混元生 3D 专业版：Geometry 生成白模；默认返回 GLB。
        using var request = NewRequest(SubmitUri, credentials.Decrypt(provider.ApiKeyCipher));
        request.Content = JsonContent.Create(new
        {
            Model = provider.Model,
            GenerateType = "Geometry",
            ImageUrl = new { Url = "data:" + mime + ";base64," + Convert.ToBase64String(image.Span) }
        }, options: new JsonSerializerOptions(JsonSerializerDefaults.General));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var payload = await ReadJsonAsync(response, ct);
        var body = Unwrap(payload.RootElement);
        if (!body.TryGetProperty("JobId", out var id) || id.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(id.GetString()))
            throw new InvalidDataException("混元提交未返回 JobId；结果不确定，禁止自动重复提交");
        return id.GetString()!;
    }

    /// <summary>根据已持久化 JobId 查询 WAIT/RUN/DONE/FAIL；只接受 GLB 类型结果。</summary>
    public async Task<Hunyuan3DResult> QueryAsync(
        AiGenerationSettings settings, string jobId, CancellationToken ct = default)
    {
        var provider = VerifyProvider(settings);
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("混元 JobId 不能为空", nameof(jobId));
        using var request = NewRequest(QueryUri, credentials.Decrypt(provider.ApiKeyCipher));
        request.Content = JsonContent.Create(new { JobId = jobId }, options: new JsonSerializerOptions(JsonSerializerDefaults.General));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var payload = await ReadJsonAsync(response, ct);
        var body = Unwrap(payload.RootElement);
        if (body.TryGetProperty("Error", out var error) && error.ValueKind == JsonValueKind.Object)
            throw new InvalidOperationException("查询混元任务失败，稍后继续查询同一 JobId");
        var status = body.GetProperty("Status").GetString();
        if (status is not ("WAIT" or "RUN" or "DONE" or "FAIL"))
            throw new InvalidDataException("混元查询状态不受支持");
        if (status == "FAIL")
        {
            var code = body.TryGetProperty("ErrorCode", out var c) ? c.GetString() : null;
            return new Hunyuan3DResult(status, null, string.IsNullOrWhiteSpace(code) ? "混元任务失败" : "混元任务失败：" + code[..Math.Min(code.Length, 100)]);
        }
        if (status != "DONE") return new Hunyuan3DResult(status, null, null);

        if (!body.TryGetProperty("ResultFile3Ds", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("混元已完成但没有返回模型文件");
        foreach (var file in files.EnumerateArray())
        {
            if (file.TryGetProperty("Type", out var type) &&
                string.Equals(type.GetString(), "GLB", StringComparison.OrdinalIgnoreCase) &&
                file.TryGetProperty("Url", out var url) && url.ValueKind == JsonValueKind.String)
            {
                return new Hunyuan3DResult("DONE", url.GetString(), null);
            }
        }

        throw new InvalidDataException("混元结果没有 GLB 文件，无法供小程序预览");
    }

    private static AiProviderSettings VerifyProvider(AiGenerationSettings settings)
    {
        if (!settings.CanGenerateModel() || settings.Provider is null)
            throw new InvalidOperationException("尚未配置可用的混元 3D 专业版");
        return settings.Provider;
    }

    private static HttpRequestMessage NewRequest(Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        // 混元兼容接口文档使用 Authorization: sk-...，不能自动加 Bearer。
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);
        return request;
    }

    private static JsonElement Unwrap(JsonElement root)
        => root.TryGetProperty("Response", out var response) ? response : root;

    private static string? DetectMime(ReadOnlySpan<byte> image)
    {
        if (image.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (image.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return "image/jpeg";
        if (image.Length >= 12 && image[..4].SequenceEqual("RIFF"u8) &&
            image.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var data = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(data, ct)) > 0)
        {
            if (buffer.Length + read > 1024 * 1024) throw new InvalidDataException("混元 API 响应过大");
            buffer.Write(data, 0, read);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: ct);
    }
}
