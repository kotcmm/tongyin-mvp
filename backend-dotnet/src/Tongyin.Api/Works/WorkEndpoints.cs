using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Media;

namespace Tongyin.Api.Works;

/// <summary>
/// 兼容微信小程序上传协议的 HTTP 入口，仅处理请求解析和响应转换。
/// </summary>
public static class WorkEndpoints
{
    private const long MaxRequestBytes = OriginalImageLimits.MaxBytes + (1L << 20);

    /// <summary>
    /// 注册作品创建和签名原画下载端点。
    /// </summary>
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/works", CreateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxRequestBytes })
            .RequireAuthorization(AuthorizationPolicies.Customer);
        app.MapGet("/media/{file}", GetMedia);
    }

    private static async Task<IResult> CreateAsync(HttpContext context, CreateWork create, LocalMediaStore media, ILoggerFactory loggerFactory)
    {
        var cancellationToken = context.RequestAborted;
        var (file, error) = await ReadUploadAsync(context.Request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        try
        {
            await using var image = file!.OpenReadStream();
            var owner = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
            var work = await create.ExecuteFromImageAsync(owner, image, file.Length, cancellationToken);
            return Results.Json(WorkResponse.FromCreated(work, media.SignUrl(work.Original)), statusCode: 201);
        }
        catch (InvalidDataException)
        {
            return Failure(400, "仅支持 JPG、PNG、WebP 图片");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger(nameof(WorkEndpoints)).LogError(exception, "创建作品失败");
            return Failure(500, "保存失败");
        }
    }

    private static async Task<(IFormFile? File, IResult? Error)> ReadUploadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxRequestBytes)
        {
            return (null, Failure(413, "上传文件超过允许大小"));
        }

        if (!request.HasFormContentType)
        {
            return (null, Failure(400, "需要上传图片"));
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException exception)
        {
            return (null, Failure(exception.StatusCode == 413 ? 413 : 400, "解析上传请求失败"));
        }
        catch (InvalidDataException)
        {
            return (null, Failure(400, "解析上传请求失败"));
        }

        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return (null, Failure(400, "未找到上传文件或上传文件为空"));
        }

        if (file.Length > OriginalImageLimits.MaxBytes)
        {
            return (null, Failure(413, "图片最多 10 MiB"));
        }

        return (file, null);
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
