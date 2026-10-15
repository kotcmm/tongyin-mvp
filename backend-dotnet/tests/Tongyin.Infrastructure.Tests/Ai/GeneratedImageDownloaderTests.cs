using System.Net;
using System.Text;
using Tongyin.Infrastructure.Ai;
using Tongyin.Infrastructure.Media;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Ai;

/// <summary>
/// 验证单张生成图片通过 HTTPS 下载并存入童印媒体目录，全程不访问公网。
/// </summary>
public sealed class GeneratedImageDownloaderTests
{
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 1, 2, 3, 4, 5, 6];

    /// <summary>
    /// 保存后必须得到本地媒体文件名，而不是把外部临时 URL 保存进作品。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ValidPng_SavesLocalMedia()
    {
        using var media = new TempMedia();
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://images.example/generated.png", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(Respond(HttpStatusCode.OK, Png, "text/plain"));
        });
        using var client = new HttpClient(handler);
        var downloader = new GeneratedImageDownloader(client, media.Store);

        var fileName = await downloader.DownloadAsync("https://images.example/generated.png");

        Assert.EndsWith(".png", fileName);
        Assert.DoesNotContain("/", fileName);
        Assert.Equal(Png, File.ReadAllBytes(media.FullPath(fileName)));
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// 不得根据声明的 Content-Type 来决定文件类型，真正内容是 JPEG 时应存 JPG。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_JpegContent_UsesDetectedExtension()
    {
        using var media = new TempMedia();
        var handler = new StubHandler((_, _) => Task.FromResult(Respond(
            HttpStatusCode.OK, [0xFF, 0xD8, 0xFF, 0x00, 0x01], "image/png")));
        using var client = new HttpClient(handler);

        var fileName = await new GeneratedImageDownloader(client, media.Store)
            .DownloadAsync("https://images.example/signed");

        Assert.EndsWith(".jpg", fileName);
        Assert.Single(media.Files());
    }

    /// <summary>
    /// 任何非图像内容都不得留在媒体目录。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_InvalidContent_DoesNotCreateFile()
    {
        using var media = new TempMedia();
        var handler = new StubHandler((_, _) => Task.FromResult(Respond(
            HttpStatusCode.OK, Encoding.UTF8.GetBytes("<html>not an image</html>"), "image/png")));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GeneratedImageDownloader(client, media.Store).DownloadAsync("https://images.example/invalid"));

        Assert.Empty(media.Files());
    }

    /// <summary>
    /// 内容为空时不能生成作品媒体文件。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_EmptyResponse_Rejects()
    {
        using var media = new TempMedia();
        var handler = new StubHandler((_, _) => Task.FromResult(Respond(HttpStatusCode.OK, [])));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GeneratedImageDownloader(client, media.Store).DownloadAsync("https://images.example/empty"));

        Assert.Empty(media.Files());
    }

    /// <summary>
    /// 不能只信任服务商声明的文件长度，10 MiB 以上的实际响应必须拒绝。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ChunkedOversize_RejectsWithoutSaving()
    {
        using var media = new TempMedia();
        var body = new byte[(10 * 1024 * 1024) + 1];
        Png.CopyTo(body, 0);
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(body))
        }));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GeneratedImageDownloader(client, media.Store).DownloadAsync("https://images.example/oversize"));

        Assert.Empty(media.Files());
    }

    /// <summary>
    /// 已知 Content-Length 超额时无需读取响应体即可拒绝。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_DeclaredOversize_RejectsWithoutSaving()
    {
        using var media = new TempMedia();
        var handler = new StubHandler((_, _) =>
        {
            var response = Respond(HttpStatusCode.OK, Png);
            response.Content.Headers.ContentLength = 10 * 1024 * 1024 + 1;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GeneratedImageDownloader(client, media.Store).DownloadAsync("https://images.example/oversize"));

        Assert.Empty(media.Files());
    }

    /// <summary>
    /// 上游失败时不泄露带签名参数的下载链接，也不能保存错误页面。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_UpstreamError_ReportsStatusWithoutUrl()
    {
        using var media = new TempMedia();
        var handler = new StubHandler((_, _) => Task.FromResult(Respond(HttpStatusCode.BadGateway, Encoding.UTF8.GetBytes("secret body"))));
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GeneratedImageDownloader(client, media.Store).DownloadAsync("https://images.example/x?signature=sensitive"));

        Assert.Contains("502", exception.Message);
        Assert.DoesNotContain("signature", exception.ToString());
        Assert.DoesNotContain("secret body", exception.ToString());
        Assert.Empty(media.Files());
    }

    /// <summary>
    /// 结果地址不允许 HTTP、内网 IP、带用户名的 URL 或 localhost。
    /// </summary>
    [Theory]
    [InlineData("http://images.example/example.png")]
    [InlineData("https://localhost/example.png")]
    [InlineData("https://127.0.0.1/example.png")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://192.168.1.1/img.png")]
    [InlineData("https://[::1]/img.png")]
    [InlineData("https://user:password@images.example/a.png")]
    [InlineData("https://images.example:8443/a.png")]
    public async Task DownloadAsync_UntrustedAddress_RejectsBeforeHttp(string url)
    {
        using var media = new TempMedia();
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("不得发出请求"));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GeneratedImageDownloader(client, media.Store).DownloadAsync(url));

        Assert.Equal(0, handler.Calls);
    }

    /// <summary>
    /// 不允许根据上游 Location 自动访问第二个地址，包括改为 HTTP 或内网。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_Redirect_RejectsWithoutFollowing()
    {
        using var media = new TempMedia();
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("http://127.0.0.1/private") }
        }));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GeneratedImageDownloader(client, media.Store).DownloadAsync("https://images.example/a.png"));

        Assert.Equal(1, handler.Calls);
        Assert.Empty(media.Files());
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, byte[] content, string type = "image/png")
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(content) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
        return response;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return callback(request, cancellationToken);
        }
    }

    private sealed class TempMedia : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "tongyin-generated-" + Guid.NewGuid().ToString("N"));

        public LocalMediaStore Store => new(_path, "https://tongyin.example", "test-session-secret-1234567890");

        public string FullPath(string name) => Path.Combine(_path, "media", name);

        public string[] Files() => Directory.Exists(Path.Combine(_path, "media"))
            ? Directory.GetFiles(Path.Combine(_path, "media"))
            : [];

        public void Dispose()
        {
            if (Directory.Exists(_path))
                Directory.Delete(_path, recursive: true);
        }
    }
}
