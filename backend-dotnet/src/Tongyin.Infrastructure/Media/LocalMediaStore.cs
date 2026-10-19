using System.Buffers.Text;
using System.Buffers.Binary;
using Tongyin.Application.Works;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tongyin.Infrastructure.Media;

/// <summary>
/// 管理本地图片存储以及带签名的媒体访问。
/// </summary>
public sealed class LocalMediaStore : IOriginalImageStore, IOriginalImageReader, IModelUploadStore
{
    /// <summary>单个 GLB 模型最大体积为 100 MiB。</summary>
    public const long MaxGlbBytes = 100L * 1024 * 1024;
    private static readonly byte[] _pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] _jpegSignature = [0xFF, 0xD8, 0xFF];
    private readonly string _directory;
    private readonly string _publicUrl;
    private readonly byte[] _secret;

    /// <summary>
    /// 配置媒体存储目录、公开访问地址和签名密钥。
    /// </summary>
    public LocalMediaStore(string dataDirectory, string publicUrl, string secret)
    {
        _directory = Path.Combine(dataDirectory, "media");
        _publicUrl = publicUrl.TrimEnd('/');
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>
    /// 通过真实文件头识别 JPG、PNG 或 WebP，并保存不超过 10 MiB 的原画。
    /// </summary>
    public async Task<string> SaveImageAsync(Stream source, long length, CancellationToken cancellationToken = default)
    {
        if (length == 0 || length > ImageFileLimits.MaxBytes)
        {
            throw new InvalidDataException("图片为空或超过 10 MiB");
        }

        var header = new byte[12];
        var count = await source.ReadAtLeastAsync(header.AsMemory(), header.Length, throwOnEndOfStream: false, cancellationToken);
        var extension = DetectImageExtension(header.AsSpan(0, count))
            ?? throw new InvalidDataException("仅支持 JPG、PNG、WebP 图片");

        Directory.CreateDirectory(_directory);
        var name = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant() + extension;
        var path = Path.Combine(_directory, name);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await output.WriteAsync(header.AsMemory(0, count), cancellationToken);
            await source.CopyToAsync(output, cancellationToken);
            return name;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    /// <summary>
    /// 只根据本地安全媒体文件名读取已保存的原画，并限制读取容量。
    /// </summary>
    public async Task<byte[]> ReadImageAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 100
            || fileName.Contains("..", StringComparison.Ordinal)
            || fileName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
        {
            throw new InvalidDataException("原画文件名无效");
        }

        await using var source = new FileStream(Path.Combine(_directory, fileName), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (source.Length == 0 || source.Length > ImageFileLimits.MaxBytes)
        {
            throw new InvalidDataException("原画为空或超过 10 MiB");
        }

        var bytes = new byte[checked((int)source.Length)];
        await source.ReadExactlyAsync(bytes.AsMemory(), cancellationToken);
        return bytes;
    }

    /// <summary>
    /// 校验 glTF 2.0 二进制 GLB 头部和 JSON 块，再将模型保存到同一媒体目录。
    /// </summary>
    public async Task<string> SaveGlbAsync(Stream source, long length, CancellationToken cancellationToken = default)
    {
        if (length < 24 || length > MaxGlbBytes || length > uint.MaxValue)
        {
            throw new InvalidDataException("GLB 文件大小无效");
        }

        var header = new byte[20];
        var count = await source.ReadAtLeastAsync(header.AsMemory(), header.Length, throwOnEndOfStream: false, cancellationToken);
        if (count != 20
            || !header.AsSpan(0, 4).SequenceEqual("glTF"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)) != 2
            || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4)) != length
            || !header.AsSpan(16, 4).SequenceEqual("JSON"u8))
        {
            throw new InvalidDataException("仅支持有效的 glTF 2.0 GLB 模型");
        }

        var jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
        if (jsonLength < 4 || jsonLength % 4 != 0 || 20L + jsonLength > length)
        {
            throw new InvalidDataException("GLB JSON 块长度无效");
        }

        Directory.CreateDirectory(_directory);
        var name = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant() + ".glb";
        var path = Path.Combine(_directory, name);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await output.WriteAsync(header, cancellationToken);
            await source.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            if (output.Length != length)
            {
                throw new InvalidDataException("GLB 实际长度与头部不一致");
            }
            return name;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    /// <summary>
    /// 撤销未关联到作品的已上传原画。
    /// </summary>
    public void Delete(string fileName)
    {
        File.Delete(Path.Combine(_directory, fileName));
    }

    /// <summary>
    /// 检查媒体是否为明确的本地 JPG/PNG/WebP/GLB 文件名，不接受目录穿越或外部 URL。
    /// </summary>
    public static bool IsStoredMediaName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100
            || name.Contains("..", StringComparison.Ordinal)
            || name.Contains('/') || name.Contains('\\'))
        {
            return false;
        }

        var dot = name.LastIndexOf('.');
        if (dot < 1 || dot > 90)
        {
            return false;
        }

        var extension = name[dot..].ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".glb"))
        {
            return false;
        }

        return name.AsSpan(0, dot).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-") < 0;
    }

    /// <summary>
    /// 仅限持久化媒体清理器调用；拒绝任意相对/绝对路径。
    /// 不存在的文件可重复删除，失败时让后台任务保留以便重试。
    /// </summary>
    public void DeleteStoredMedia(string fileName)
    {
        if (!IsStoredMediaName(fileName))
        {
            throw new InvalidDataException("不允许删除不安全的媒体文件名");
        }

        File.Delete(Path.Combine(_directory, fileName));
    }

    /// <summary>
    /// 为已存储的媒体签发有效期为 48 小时的访问地址。
    /// </summary>
    public string SignUrl(string fileName)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(48).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(fileName + ":" + expires));
        return _publicUrl + "/media/" + fileName + "?expires=" + expires + "&signature=" + Base64Url.EncodeToString(signature);
    }

    /// <summary>
    /// 返回当前网站可访问的相对签名媒体地址，用于运营后台避免配置域名或端口与实际访问来源不一致。
    /// </summary>
    public string SignRelativeUrl(string fileName)
        => SignUrl(fileName)[_publicUrl.Length..];

    /// <summary>
    /// 验证签名和文件名称后，返回对应的本地媒体路径和内容类型。
    /// </summary>
    public bool TryGet(string fileName, string expires, string signature, out string path, out string contentType)
    {
        path = string.Empty;
        contentType = string.Empty;
        if (string.IsNullOrEmpty(fileName) || fileName.Length > 100 || fileName.Contains("..", StringComparison.Ordinal)
            || fileName.Contains('/') || fileName.Contains('\\')
            || !long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
            || expiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            || string.IsNullOrEmpty(signature) || signature.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')))
        {
            return false;
        }

        var decoded = new byte[signature.Length];
        if (!Base64Url.TryDecodeFromChars(signature, decoded, out var length))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(fileName + ":" + expires));
        if (!CryptographicOperations.FixedTimeEquals(expected, decoded.AsSpan(0, length)))
        {
            return false;
        }

        var fullPath = Path.Combine(_directory, fileName);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        path = fullPath;
        contentType = Path.GetExtension(fileName) switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".webp" => "image/webp",
            ".glb" => "model/gltf-binary",
            _ => "application/octet-stream"
        };
        return true;
    }

    private static string? DetectImageExtension(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(_pngSignature))
        {
            return ".png";
        }

        if (header.StartsWith(_jpegSignature))
        {
            return ".jpg";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }
}
