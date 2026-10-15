using Microsoft.AspNetCore.Mvc;
using Tongyin.Infrastructure.Media;

namespace Tongyin.Api.Controllers;

/// <summary>
/// 对外提供带签名且有有效期的媒体下载，与微信用户及后台管理员登录分别隔离。
/// </summary>
[ApiController]
public sealed class MediaController : ControllerBase
{
    private readonly LocalMediaStore _media;

    /// <summary>
    /// 通过依赖注入获得媒体文件的签名验证与路径解析能力。
    /// </summary>
    public MediaController(LocalMediaStore media)
    {
        _media = media;
    }

    /// <summary>
    /// 仅当文件名、过期时间和签名均有效时返回媒体内容。
    /// </summary>
    [HttpGet("/media/{file}")]
    public IActionResult Get([FromRoute] string file, [FromQuery] string? expires, [FromQuery] string? signature)
    {
        if (!_media.TryGet(file, expires ?? string.Empty, signature ?? string.Empty, out var path, out var contentType))
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private,max-age=120";
        return PhysicalFile(Path.GetFullPath(path), contentType);
    }
}
