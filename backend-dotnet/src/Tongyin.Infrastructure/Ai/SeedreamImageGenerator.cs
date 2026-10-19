using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tongyin.Application.Ai;
using Tongyin.Application.Works;

namespace Tongyin.Infrastructure.Ai;

/// <summary>
/// Seedream 单图生成适配器。仅负责服务商请求和响应，不操作作品数据库或下载生成文件。
/// </summary>
public sealed class SeedreamImageGenerator(HttpClient httpClient, AiCredentialProtector decryptor) : IImageGenerationClient
{
    private const string OfficialHost = "ark.cn-beijing.volces.com";
    private const string OfficialPath = "/api/v3/images/generations";

    /// <summary>
    /// 使用已配置的服务商生成一张图片，返回 HTTPS 结果地址；网络异常后不会自动重试以免重复计费。
    /// </summary>
    public async Task<string> GenerateAsync(AiGenerationSettings settings, ReadOnlyMemory<byte> originalImage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var provider = RequireProvider(settings);
        var endpoint = RequireOfficialEndpoint(provider.Endpoint);
        if (originalImage.Length == 0 || originalImage.Length > ImageFileLimits.MaxBytes)
        {
            throw new InvalidDataException("原画为空或超出10MiB限制");
        }

        var mime = DetectMime(originalImage.Span) ?? throw new InvalidDataException("原画必须是 PNG、JPEG 或 WebP");

        using var request = CreateRequest(endpoint, provider.Model, settings.PromptTemplate, mime, originalImage.Span, decryptor.Decrypt(provider.ApiKeyCipher));
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var code = await ReadSafeErrorCodeAsync(response.Content, cancellationToken);
            throw new InvalidOperationException($"Seedream HTTP {(int)response.StatusCode} ({code})");
        }

        return await ReadImageUrlAsync(response.Content, cancellationToken);
    }

    private static AiProviderSettings RequireProvider(AiGenerationSettings settings)
    {
        var provider = settings.Provider;
        if (settings.Stage != "image" || provider is null || provider.Stage != "image"
            || provider.Adapter != "seedream" || !provider.Enabled || !provider.Default)
        {
            throw new InvalidOperationException("未配置可用的 Seedream 图片服务商");
        }

        return provider;
    }

    private static Uri RequireOfficialEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Host != OfficialHost
            || uri.AbsolutePath != OfficialPath || uri.Port != 443
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException("Seedream 仅支持方舟官方 HTTPS 图片 API 地址");
        }

        return uri;
    }

    private static string? DetectMime(ReadOnlySpan<byte> image)
    {
        if (image.Length >= 8 && image[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return "image/png";
        }

        if (image.Length >= 3 && image[0] == 0xFF && image[1] == 0xD8 && image[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (image.Length >= 12 && image[..4].SequenceEqual("RIFF"u8) && image.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private static HttpRequestMessage CreateRequest(Uri endpoint, string model, string prompt, string mime, ReadOnlySpan<byte> image, string apiKey)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
            prompt,
            image = $"data:{mime};base64,{Convert.ToBase64String(image)}",
            size = "2K",
            response_format = "url",
            watermark = false
        });

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Seedream 请求超时，可能已计费，请先核对结果");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Seedream 连接失败，可能已计费，请先核对结果");
        }
    }

    private static async Task<string> ReadImageUrlAsync(HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            await content.LoadIntoBufferAsync(2 * 1024 * 1024, cancellationToken);
            using var stream = await content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("data", out var images)
                || images.ValueKind != JsonValueKind.Array || images.GetArrayLength() != 1)
            {
                throw new InvalidOperationException("Seedream 未返回单张有效图片");
            }

            var image = images[0];
            if (image.ValueKind != JsonValueKind.Object
                || !image.TryGetProperty("url", out var property)
                || property.ValueKind != JsonValueKind.String
                || !Uri.TryCreate(property.GetString(), UriKind.Absolute, out var url)
                || url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length != 0)
            {
                throw new InvalidOperationException("Seedream 未返回单张有效图片");
            }

            return url.AbsoluteUri;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Seedream 响应格式无效");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Seedream 响应读取失败");
        }
    }

    private static async Task<string> ReadSafeErrorCodeAsync(HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            await content.LoadIntoBufferAsync(32 * 1024, cancellationToken);
            using var stream = await content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String)
            {
                var text = code.GetString();
                if (!string.IsNullOrEmpty(text) && text.Length <= 100
                    && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_'))
                {
                    return text;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException)
        {
            // 上游错误正文不可信，不向业务调用方泄漏原始内容。
        }

        return "UnknownError";
    }
}
