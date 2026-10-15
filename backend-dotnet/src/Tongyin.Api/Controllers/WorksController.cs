using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Api.ViewModels.Works;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Media;

namespace Tongyin.Api.Controllers;

/// <summary>
/// 微信小程序作品接口，复用微信授权策略，只负责 HTTP 请求、业务调用及响应转换。
/// </summary>
[ApiController]
[Route("api/works")]
[WechatAuthorize]
public sealed class WorksController : ControllerBase
{
    private const long MaxRequestBytes = OriginalImageLimits.MaxBytes + (1L << 20);

    private readonly CreateWork _createWork;
    private readonly ListWorks _listWorks;
    private readonly LocalMediaStore _media;
    private readonly ILogger<WorksController> _logger;

    /// <summary>
    /// 通过依赖注入获得创建和查询用例、媒体访问服务及日志。
    /// </summary>
    public WorksController(CreateWork createWork, ListWorks listWorks, LocalMediaStore media, ILogger<WorksController> logger)
    {
        _createWork = createWork;
        _listWorks = listWorks;
        _media = media;
        _logger = logger;
    }

    /// <summary>
    /// 上传一张儿童原画并创建作品，保持原小程序的响应字段及 HTTP 状态码。
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<WorkResponse>> CreateAsync([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "未找到上传文件或上传文件为空" });
        }

        if (file.Length > OriginalImageLimits.MaxBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "图片最多 10 MiB" });
        }

        try
        {
            await using var image = file.OpenReadStream();
            var owner = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
            var work = await _createWork.ExecuteFromImageAsync(owner, image, file.Length, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, WorkResponse.FromCreated(work, _media.SignUrl(work.Original)));
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { error = "仅支持 JPG、PNG、WebP 图片" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "创建作品失败");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "保存失败" });
        }
    }

    /// <summary>
    /// 查询当前微信用户自己的全部作品，每次为媒体文件生成新的签名地址。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<WorkResponse[]>> ListAsync(CancellationToken cancellationToken)
    {
        var owner = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        var works = await _listWorks.ExecuteAsync(owner, cancellationToken);
        return Ok(works.Select(work => WorkResponse.FromSnapshot(work, _media.SignUrl)).ToArray());
    }


}
