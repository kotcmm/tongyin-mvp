using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tongyin.Application.Ai;

namespace Tongyin.Infrastructure.Ai;

/// <summary>
/// Seedream 图片生成 HTTP 适配器：单张输入、单张结果；不处理数据库及文件下载。
/// </summary>
public sealed class SeedreamImageGenerator(HttpClient httpClient, GoAiKeyDecryptor decryptor)
{
    private const int MaxImageSize = 10 * 1024 * 1024;
    private const string Host = "ark.cn-beijing.volces.com";
    private const string Path = "/api/v3/images/generations";

    /// <summary>
    /// 使用已配置服务商与提示词提交原画，返回唯一的 HTTPS 结果地址；失败不自动重试。
    /// </summary>
    public async Task<string> GenerateAsync(AiGenerationSettings settings, ReadOnlyMemory<byte> originalImage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var provider = settings.Provider;
        if (settings.Stage != "image" || provider is null || provider.Stage != "image"
            || provider.Adapter != "seedream" || !provider.Enabled || !provider.Default)
        {
            throw new InvalidOperationException("未配置可用的 Seedream 图片服务商");
        }

        if (!Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Host != Host
            || endpoint.AbsolutePath != Path || endpoint.Port != 443
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            throw new InvalidOperationException("Seedream 仅支持方舟官方 HTTPS 图片 API 地址");
        }

        if (originalImage.Length == 0 || originalImage.Length > MaxImageSize)
        {
            throw new InvalidDataException("原画为空或超出10MiB限制");
        }

        var mime = DetectMime(originalImage.Span) ?? throw new InvalidDataException("原画必须是 PNG、JPEG 或 WebP");
        var token = decryptor.Decrypt(provider.ApiKeyCipher);
        var json = JsonSerializer.Serialize(new
        {
            model = provider.Model,
            prompt = settings.PromptTemplate,
            image = "data:" + mime + ";base64," + Convert.ToBase64String(originalImage.Span),
            size = "2K",
            response_format = "url",
            watermark = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Seedream 请求超时，可能已计费，请先核对结果");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Seedream 连接失败，可能已计费，请先核对结果");
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var code = await SafeErrorCodeAsync(response.Content, cancellationToken);
                throw new InvalidOperationException($"Seedream HTTP {(int)response.StatusCode} ({code})");
            }

            try
            {
                await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, cancellationToken);
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != 1)
                {
                    throw new InvalidOperationException("Seedream 未返回单张有效图片");
                }

                var item = data[0];
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("url", out var property)
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
    }

    private static string? DetectMime(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] {137, 80, 78, 71, 13, 10, 26, 10}))
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }

    private static async Task<string> SafeErrorCodeAsync(HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            await content.LoadIntoBufferAsync(32 * 1024, cancellationToken);
            using var stream = await content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
            {
                var text = code.GetString();
                if (!string.IsNullOrEmpty(text) && text.Length <= 100
                    && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_'))
                    return text;
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException)
        {
            // 丢弃不可信的上游错误正文。
        }
        return "UnknownError";
    }
}
