using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.ViewModels.Ai;
using Tongyin.Api.Auth;
using Tongyin.Api.Http;
using Tongyin.Application.Works;

namespace Tongyin.Api.Controllers;

/// <summary>
/// 管理员 AI 任务核查入口，只允许人工确认失败或补录已生成图片，禁止重新调用模型。
/// </summary>
[ApiController]
[Route("api/admin/ai")]
[AdminAuthorize]
public sealed class AdminAiTasksController(ListStalledImageTasks listTasks, ReviewStalledImageTask review) : ControllerBase
{
    /// <summary>
    /// 查询认领超过 15 分钟仍未完成的图片任务；不改变状态、不触发模型计费。
    /// </summary>
    [HttpGet("stalled-image-tasks")]
    public async Task<IActionResult> GetStalledAsync(CancellationToken cancellationToken)
    {
        var tasks = await listTasks.ExecuteAsync(DateTimeOffset.UtcNow.AddMinutes(-15), 100, cancellationToken);
        return Ok(tasks);
    }
    /// <summary>
    /// 确认已核实的超时图片任务失败；只改变当前任务状态并写入管理员审计，不触发生成。
    /// </summary>
    [HttpPost("stalled-image-tasks/{workId}/fail")]
    public async Task<IActionResult> ConfirmFailureAsync([FromRoute] string workId, [FromBody] AdminImageFailureReviewRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await review.ConfirmFailureAsync(
                workId, request.GenerationId ?? "", User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
                request.Reason ?? "", cancellationToken);
            return Ok(new { status = result.Status, generation_id = result.GenerationId });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    /// <summary>
    /// 管理员上传已由服务商生成的图片并确认成功；回写失败时回收本次上传文件。
    /// </summary>
    [HttpPost("stalled-image-tasks/{workId}/complete")]
    [RequestSizeLimit(UploadRequestLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimits.MaxRequestBytes)]
    public async Task<IActionResult> ConfirmImageAsync([FromRoute] string workId, [FromForm] AdminImageCompletionReviewRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(new { error = "需要上传已生成的图片" });
        }

        if (request.File.Length > ImageFileLimits.MaxBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "图片最多 10 MiB" });
        }

        try
        {
            await using var stream = request.File.OpenReadStream();
            var result = await review.CompleteFromUploadAsync(
                workId, request.GenerationId ?? "", User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
                request.Reason ?? "", stream, request.File.Length, cancellationToken);
            return Ok(new { status = result.Status, generation_id = result.GenerationId });
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

}
