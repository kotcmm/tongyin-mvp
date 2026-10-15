using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Auth;

namespace Tongyin.Api.Works;

/// <summary>
/// 兼容微信小程序现有协议的作品上传与原画访问端点。
/// </summary>
public static class WorkEndpoints
{
    private const long MaxImageBytes = 10L << 20;
    private const long MaxRequestBytes = MaxImageBytes + (1L << 20);

    /// <summary>
    /// 注册作品创建与其已保存原画的签名访问地址。
    /// </summary>
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/works", CreateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxRequestBytes });
        app.MapGet("/media/{file}", GetMedia);
    }

    private static async Task<IResult> CreateAsync(HttpContext context, GoSessionTokenValidator tokens, SqlUserAccess users, CreateWork create, IConfiguration settings)
    {
        var cancellationToken = context.RequestAborted;
        var auth = context.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.Ordinal)
            || !tokens.TryValidate(auth["Bearer ".Length..], DateTimeOffset.UtcNow, out var owner))
        {
            return Failure(401, "请登录");
        }

        if (!await users.IsEnabledAsync(owner, cancellationToken))
        {
            return Failure(403, "账号已停用或用户不存在");
        }

        if (context.Request.ContentLength > MaxRequestBytes)
        {
            return Failure(413, "上传文件超过允许大小");
        }

        if (!context.Request.HasFormContentType)
        {
            return Failure(400, "需要上传图片");
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Failure(413, "上传文件超过允许大小");
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        {
            return Failure(413, "上传文件超过允许大小");
        }

        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Failure(400, "未找到上传文件或上传文件为空");
        }

        if (file.Length > MaxImageBytes)
        {
            return Failure(413, "图片最多 10 MiB");
        }

        await using var input = file.OpenReadStream();
        var header = new byte[12];
        var headerLength = await input.ReadAsync(header, cancellationToken);
        var extension = DetectImageExtension(header.AsSpan(0, headerLength));
        if (extension is null)
        {
            return Failure(400, "仅支持 JPG、PNG、WebP 图片");
        }

        var directory = Path.Combine(settings["DATA_DIR"] ?? "./data", "media");
        Directory.CreateDirectory(directory);
        var fileName = NewId() + extension;
        var target = Path.Combine(directory, fileName);
        try
        {
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await output.WriteAsync(header.AsMemory(0, headerLength), cancellationToken);
                await input.CopyToAsync(output, cancellationToken);
            }

            var work = await create.ExecuteAsync(NewId(), owner, DateTimeOffset.UtcNow, fileName, cancellationToken);
            var signedOriginal = SignMediaUrl(fileName, settings);
            var timestamp = work.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
            return Results.Json(new
            {
                id = work.Id,
                created_at = timestamp,
                original = signedOriginal,
                candidates = Array.Empty<string>(),
                selected = work.Selected,
                preview = Array.Empty<string>(),
                model = work.Model,
                status = "image_pending",
                image_attempts = work.ImageAttempts,
                generation_id = work.GenerationId,
                note = work.Note
            }, statusCode: 201);
        }
        catch
        {
            File.Delete(target);
            return Failure(500, "保存失败");
        }
    }

    private static IResult GetMedia(string file, HttpContext context, IConfiguration settings)
    {
        if (string.IsNullOrEmpty(file) || file.Length > 100 || file.Contains("..", StringComparison.Ordinal)
            || file.Contains('/') || file.Contains('\\'))
        {
            return Results.NotFound();
        }

        var expires = context.Request.Query["expires"].ToString();
        var signature = context.Request.Query["signature"].ToString();
        if (!long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
            || expiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            || !IsValidMediaSignature(file, expires, signature, settings["SESSION_SECRET"]!))
        {
            return Results.NotFound();
        }

        var target = Path.Combine(settings["DATA_DIR"] ?? "./data", "media", file);
        if (!File.Exists(target))
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "private,max-age=120";
        var contentType = Path.GetExtension(file) switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
        return Results.File(target, contentType);
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

    private static string SignMediaUrl(string name, IConfiguration settings)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(48).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var payload = Encoding.UTF8.GetBytes(name + ":" + expires);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings["SESSION_SECRET"]!), payload);
        var origin = (settings["PUBLIC_URL"] ?? "http://localhost:8080").TrimEnd('/');
        return origin + "/media/" + name + "?expires=" + expires + "&signature=" + Base64Url.EncodeToString(signature);
    }

    private static bool IsValidMediaSignature(string name, string expires, string signature, string secret)
    {
        if (string.IsNullOrEmpty(signature) || signature.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')))
        {
            return false;
        }

        var bytes = new byte[signature.Length];
        if (!Base64Url.TryDecodeFromChars(signature, bytes, out var decodedLength))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(name + ":" + expires));
        return CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(0, decodedLength));
    }

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static IResult Failure(int status, string reason) => Results.Json(new { error = reason }, statusCode: status);
}
