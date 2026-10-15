using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Media;

namespace Tongyin.Api.Works;

/// <summary>
/// 兼容微信小程序旧版作品上传协议的 HTTP 入口。
/// </summary>
public static class WorkEndpoints
{
    private const long MaxImageBytes = 10L << 20;
    private const long MaxRequestBytes = MaxImageBytes + (1L << 20);

    /// <summary>
    /// 注册作品创建和经过媒体签名校验的原画下载端点。
    /// </summary>
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/works", CreateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxRequestBytes })
            .RequireAuthorization("EnabledWechatUser");
        app.MapGet("/media/{file}", GetMedia);
    }

    private static async Task<IResult> CreateAsync(HttpContext context, LocalMediaStore media, CreateWork create)
    {
        var cancellationToken = context.RequestAborted;
        var owner = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

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

        string fileName;
        try
        {
            await using var input = file.OpenReadStream();
            fileName = await media.SaveImageAsync(input, file.Length, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Failure(400, "仅支持 JPG、PNG、WebP 图片");
        }

        try
        {
            var workId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            var work = await create.ExecuteAsync(workId, owner, DateTimeOffset.UtcNow, fileName, cancellationToken);
            return Results.Json(new
            {
                id = work.Id,
                created_at = work.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
                original = media.SignUrl(fileName),
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
            media.Delete(fileName);
            return Failure(500, "保存失败");
        }
    }

    private static IResult GetMedia(string file, HttpContext context, LocalMediaStore media)
    {
        if (!media.TryGet(file, context.Request.Query["expires"].ToString(), context.Request.Query["signature"].ToString(), out var path, out var contentType))
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "private,max-age=120";
        return Results.File(path, contentType);
    }

    private static IResult Failure(int status, string reason) => Results.Json(new { error = reason }, statusCode: status);
}
