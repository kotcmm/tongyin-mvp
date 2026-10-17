using System.Buffers.Binary;
using System.Text;
using Tongyin.Infrastructure.Media;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Media;

/// <summary>覆盖 GLB 文件头校验、大小边界和小程序签名模型访问。</summary>
public sealed class LocalMediaStoreGlbTests
{
    /// <summary>最小合法 glTF 2.0 GLB 文件可以保存并通过签名链接读取。</summary>
    [Fact]
    public async Task SaveGlb_ValidModel_CanResolveSignedMedia()
    {
        var root = Path.Combine(Path.GetTempPath(), "tongyin-glb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalMediaStore(root, "https://assets.example.com", "test-session-secret");
            var bytes = ValidGlb();
            using var stream = new MemoryStream(bytes);

            var fileName = await store.SaveGlbAsync(stream, bytes.Length);

            Assert.EndsWith(".glb", fileName);
            var url = new Uri(store.SignUrl(fileName));
            var query = url.Query.TrimStart('?').Split('&')
                .Select(part => part.Split('=', 2))
                .ToDictionary(parts => parts[0], parts => parts[1]);
            Assert.True(store.TryGet(fileName, query["expires"], query["signature"], out var path, out var mime));
            Assert.Equal("model/gltf-binary", mime);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>错误魔数、版本、声明长度、JSON 块类型均必须在写盘前拒绝。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SaveGlb_InvalidHeader_DoesNotCreateModel(int variant)
    {
        var root = Path.Combine(Path.GetTempPath(), "tongyin-invalid-glb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bytes = ValidGlb();
            switch (variant)
            {
                case 0: bytes[0] = 0; break;
                case 1: BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 1); break;
                case 2: BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 999); break;
                case 3: bytes[16] = (byte)'B'; break;
            }

            var store = new LocalMediaStore(root, "https://assets.example.com", "test-session-secret");
            using var stream = new MemoryStream(bytes);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveGlbAsync(stream, bytes.Length));
            Assert.False(Directory.Exists(Path.Combine(root, "media")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>客户端声称超出 100 MiB 时直接拒绝，不分配内存或创建文件。</summary>
    [Fact]
    public async Task SaveGlb_TooLarge_RejectsBeforeRead()
    {
        var store = new LocalMediaStore(Path.GetTempPath(), "https://assets.example.com", "test-session-secret");
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveGlbAsync(stream, LocalMediaStore.MaxGlbBytes + 1));
    }

    private static byte[] ValidGlb()
    {
        var result = new byte[24];
        Encoding.ASCII.GetBytes("glTF").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12, 4), 4);
        Encoding.ASCII.GetBytes("JSON").CopyTo(result, 16);
        Encoding.ASCII.GetBytes("{}  ").CopyTo(result, 20);
        return result;
    }
}
