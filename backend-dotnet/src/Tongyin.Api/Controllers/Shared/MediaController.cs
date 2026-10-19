using Microsoft.AspNetCore.Mvc;
using Tongyin.Infrastructure.Media;

namespace Tongyin.Api.Controllers.Shared;

/// <summary>
/// 对外提供带签名且有有效期的媒体下载，与微信用户及后台管理员登录分别隔离。
/// </summary>
/// <remarks>
/// 通过依赖注入获得媒体文件的签名验证与路径解析能力。
/// </remarks>
[ApiController]
public sealed class MediaController(LocalMediaStore media) : ControllerBase
{

    /// <summary>
    /// 仅当文件名、过期时间和签名均有效时返回媒体内容。
    /// </summary>
    [HttpGet("/media/{file}")]
    public IActionResult Get([FromRoute] string file, [FromQuery] string? expires, [FromQuery] string? signature)
    {
        if (!media.TryGet(file, expires ?? string.Empty, signature ?? string.Empty, out var path, out var contentType))
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private,max-age=120";
        return PhysicalFile(Path.GetFullPath(path), contentType);
    }
}
