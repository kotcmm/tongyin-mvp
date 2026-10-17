using System.Net;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Media;

namespace Tongyin.Infrastructure.Ai;

/// <summary>从非内网公网 HTTPS 地址下载模型，限制大小后交由媒体层校验 GLB 内容。</summary>
public sealed class GeneratedModelDownloader(HttpClient httpClient, LocalMediaStore mediaStore) : IGeneratedModelStore
{
    /// <summary>下载最大 100 MiB 的模型文件，禁止跳转、代理或连接到私网 IP。</summary>
    public async Task<string> DownloadAsync(string modelUrl, CancellationToken cancellationToken = default)
    {
        var uri = RequirePublicHttpsUrl(modelUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("模型下载超时");
        }
        catch (HttpRequestException)
        {
            throw new HttpRequestException("模型下载连接失败");
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException("模型下载 HTTP 状态异常");
            }
            if (response.Content.Headers.ContentLength > LocalMediaStore.MaxGlbBytes)
            {
                throw new InvalidDataException("生成模型文件超过 100 MiB");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var block = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(block.AsMemory(), cancellationToken)) != 0)
            {
                if (buffer.Length + read > LocalMediaStore.MaxGlbBytes)
                {
                    throw new InvalidDataException("生成模型文件超过 100 MiB");
                }
                await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken);
            }

            buffer.Position = 0;
            return await mediaStore.SaveGlbAsync(buffer, buffer.Length, cancellationToken);
        }
    }

    /// <summary>回收过期任务下载的孤儿 GLB 文件。</summary>
    public void Delete(string fileName) => mediaStore.Delete(fileName);

    private static Uri RequirePublicHttpsUrl(string value)
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
            throw new InvalidDataException("模型结果只接受公网 HTTPS 地址");
        }

        return uri;
    }
}
