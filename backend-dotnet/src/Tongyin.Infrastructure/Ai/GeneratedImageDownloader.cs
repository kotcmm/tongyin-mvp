using System.Net;
using System.Net.Sockets;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Media;

namespace Tongyin.Infrastructure.Ai;

/// <summary>
/// 下载 AI 生成图片并存入童印媒体目录，只返回本地媒体文件名，不保留远端临时链接。
/// </summary>
public sealed class GeneratedImageDownloader(HttpClient httpClient, LocalMediaStore mediaStore) : IGeneratedImageStore
{
    private const long MaxImageBytes = 10L << 20;

    /// <summary>
    /// 从公开 HTTPS 地址下载一张不超过 10 MiB 的图片，文件格式由本地媒体存储验证。
    /// </summary>
    public async Task<string> DownloadAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        var uri = RequireHttpsUrl(imageUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("生成图片下载连接失败");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("生成图片下载超时");
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException($"生成图片下载失败：HTTP {(int)response.StatusCode}");
            }

            if (response.Content.Headers.ContentLength > MaxImageBytes)
            {
                throw new InvalidDataException("生成图片超过 10 MiB");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var image = await ReadBoundedAsync(stream, cancellationToken);
            return await mediaStore.SaveImageAsync(image, image.Length, cancellationToken);
        }
    }

    /// <summary>
    /// 删除未成功关联到作品的生成图片，复用本地媒体存储的文件清理规则。
    /// </summary>
    public void Delete(string fileName) => mediaStore.Delete(fileName);

    private static Uri RequireHttpsUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.HostNameType != UriHostNameType.Dns
            || uri.IsLoopback
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0
            || uri.Port != 443
            || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException("只接受公网域名的 HTTPS 图片结果地址");
        }

        return uri;
    }

    private static async Task<MemoryStream> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        var image = new MemoryStream();
        try
        {
            var buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
            {
                total += count;
                if (total > MaxImageBytes)
                {
                    throw new InvalidDataException("生成图片超过 10 MiB");
                }

                await image.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }

            if (total == 0)
            {
                throw new InvalidDataException("生成图片为空");
            }

            image.Position = 0;
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
