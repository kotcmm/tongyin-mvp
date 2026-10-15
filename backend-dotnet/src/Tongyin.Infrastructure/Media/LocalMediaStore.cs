using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tongyin.Infrastructure.Media;

/// <summary>
/// 管理本地原画上传以及兼容 Go 服务的媒体访问签名。
/// </summary>
public sealed class LocalMediaStore
{
    private const long MaxImageBytes = 10L << 20;
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
        if (length == 0 || length > MaxImageBytes)
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
    /// 撤销未关联到作品的已上传原画。
    /// </summary>
    public void Delete(string fileName)
    {
        File.Delete(Path.Combine(_directory, fileName));
    }

    /// <summary>
    /// 为已存储的媒体签发与原 Go 后端兼容的 48 小时访问地址。
    /// </summary>
    public string SignUrl(string fileName)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(48).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(fileName + ":" + expires));
        return _publicUrl + "/media/" + fileName + "?expires=" + expires + "&signature=" + Base64Url.EncodeToString(signature);
    }

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
            _ => "application/octet-stream"
        };
        return true;
    }

    private static string? DetectImageExtension(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith([137, 80, 78, 71, 13, 10, 26, 10]))
        {
            return ".png";
        }

        if (header.StartsWith([0xFF, 0xD8, 0xFF]))
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
