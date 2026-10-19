using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Api.Http;
using Tongyin.Application.Checkout;
using Tongyin.Api.ViewModels.Works;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Media;

namespace Tongyin.Api.Controllers.Miniapp;

/// <summary>
/// 微信小程序作品接口，复用微信授权策略，只负责 HTTP 请求、业务调用及响应转换。
/// </summary>
[ApiController]
[Route("api/works")]
[WechatAuthorize]
public sealed class WorksController(CreateWork createWork, ListWorks listWorks, GetWork getWork, ImageGenerationService imageGeneration, SelectWorkImage selectWorkImage, DeleteWork deleteWork, LocalMediaStore media, MiniappBusinessSettings businessSettings, ILogger<WorksController> logger) : ControllerBase
{
    /// <summary>
    /// 上传一张儿童原画并创建作品，保持原小程序的响应字段及 HTTP 状态码。
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(UploadRequestLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimits.MaxRequestBytes)]
    public async Task<ActionResult<WorkResponse>> CreateAsync([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "未找到上传文件或上传文件为空" });
        }

        if (file.Length > ImageFileLimits.MaxBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "图片最多 10 MiB" });
        }

        try
        {
            await using var image = file.OpenReadStream();
            var owner = User.GetWechatOpenId();
            var work = await createWork.ExecuteFromImageAsync(owner, image, file.Length, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, WorkResponse.FromCreated(work, media.SignUrl(work.Original)));
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
            logger.LogError(exception, "创建作品失败");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "保存失败" });
        }
    }

    /// <summary>
    /// 为当前微信用户的作品登记图片生成任务；实际生成由已启用的后台执行器异步完成。
    /// </summary>
    [HttpPost("{id}/generate")]
    public async Task<ActionResult<WorkResponse>> GenerateAsync([FromRoute] string id, CancellationToken cancellationToken)
    {
        try
        {
            var owner = User.GetWechatOpenId();
            var work = await imageGeneration.QueueAsync(owner, id, businessSettings.FreeImageAttempts, cancellationToken);
            if (work is null)
            {
                return NotFound(new { error = "作品不存在" });
            }

            return StatusCode(StatusCodes.Status202Accepted, WorkResponse.FromSnapshot(work, media.SignUrl));
        }
        catch (ImageGenerationUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    /// <summary>
    /// 用户确认单张卡通图，转入模型处理阶段；使用同一微信授权与仓储所有权校验。
    /// </summary>
    [HttpPost("{id}/select")]
    public async Task<IActionResult> SelectAsync([FromRoute] string id, [FromBody] SelectImageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await selectWorkImage.ExecuteAsync(User.GetWechatOpenId(), id, request.Index, cancellationToken);
            if (updated is null)
            {
                return NotFound(new { error = "作品不存在" });
            }

            // 本接口仅持久化用户确认，不假称模型已经成功提交到未接入的外部服务。
            return StatusCode(StatusCodes.Status202Accepted, new { status = "model_pending", message = "已进入模型处理队列" });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    /// <summary>
    /// 删除当前微信用户本人的作品。有关联订单或可能已经付费执行的任务必须保留。
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync([FromRoute] string id, CancellationToken cancellationToken)
    {
        var result = await deleteWork.ExecuteAsync(User.GetWechatOpenId(), id, cancellationToken);
        return result switch
        {
            DeleteWorkResult.Deleted => NoContent(),
            DeleteWorkResult.NotFound => NotFound(new { error = "作品不存在" }),
            DeleteWorkResult.HasOrders => Conflict(new { error = "此作品已有订单，请前往「我的订单」查看，暂不能删除" }),
            DeleteWorkResult.Processing => Conflict(new { error = "作品正在生成中，请等待任务完成后再删除" }),
            _ => throw new InvalidOperationException("未知的作品删除结果")
        };
    }

    /// <summary>
    /// 查询指定作品详情，只返回当前微信用户本人的作品；每次生成新的媒体访问签名。
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<WorkResponse>> GetAsync([FromRoute] string id, CancellationToken cancellationToken)
    {
        var owner = User.GetWechatOpenId();
        var work = await getWork.ExecuteAsync(owner, id, cancellationToken);
        if (work is null)
        {
            return NotFound(new { error = "作品不存在" });
        }

        return Ok(WorkResponse.FromSnapshot(work, media.SignUrl));
    }

    /// <summary>
    /// 查询当前微信用户自己的全部作品，每次为媒体文件生成新的签名地址。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<WorkResponse[]>> ListAsync(CancellationToken cancellationToken)
    {
        var owner = User.GetWechatOpenId();
        var works = await listWorks.ExecuteAsync(owner, cancellationToken);
        return Ok(works.Select(work => WorkResponse.FromSnapshot(work, media.SignUrl)).ToArray());
    }


}
