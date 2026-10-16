using Tongyin.Infrastructure.Media;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Media;

/// <summary>
/// 后台执行器读取已保存的原画时必须使用安全的媒体文件名并遵守 10 MiB 限额。
/// </summary>
public sealed class LocalMediaStoreOriginalReaderTests
{
    /// <summary>
    /// 有效的本地媒体文件名可读取对应原画字节。
    /// </summary>
    [Fact]
    public async Task ReadImageAsync_ValidFile_ReturnsStoredBytes()
    {
        using var files = new TestFiles();
        byte[] original = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4];
        files.Write("drawing.png", original);

        var bytes = await files.Store.ReadImageAsync("drawing.png");

        Assert.Equal(original, bytes);
    }

    /// <summary>
    /// 数据库中即使出现恶意相对路径，也不得读取媒体目录外的文件。
    /// </summary>
    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("..")]
    public async Task ReadImageAsync_UnsafeFileName_Rejects(string fileName)
    {
        using var files = new TestFiles();

        await Assert.ThrowsAsync<InvalidDataException>(() => files.Store.ReadImageAsync(fileName));
    }

    /// <summary>
    /// 原画文件超过 10 MiB 时应在读入内存前拒绝。
    /// </summary>
    [Fact]
    public async Task ReadImageAsync_OversizedFile_Rejects()
    {
        using var files = new TestFiles();
        files.Write("large.png", new byte[(10 * 1024 * 1024) + 1]);

        await Assert.ThrowsAsync<InvalidDataException>(() => files.Store.ReadImageAsync("large.png"));
    }

    private sealed class TestFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tongyin-original-reader-{Guid.NewGuid():N}");

        public LocalMediaStore Store => new(_directory, "https://tongyin.example", "test-session-secret-0123456789");

        public void Write(string fileName, byte[] content)
        {
            var folder = Path.Combine(_directory, "media");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, fileName), content);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
