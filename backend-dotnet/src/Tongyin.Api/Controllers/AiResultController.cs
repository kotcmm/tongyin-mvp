using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Api.ViewModels.Ai;
using Tongyin.Application.Works;

namespace Tongyin.Api.Controllers;

/// <summary>接受可信 worker 的 3D 生成结果，调用应用层保存模型或登记失败。</summary>
[ApiController]
[Route("api/internal/ai-result")]
[AiWebhookAuthorize]
public sealed class AiResultController(SaveModelGenerationResult saveModel) : ControllerBase
{
    /// <summary>与旧 Go worker 协议保持兼容，按任务编号拒绝过期和重复回调。</summary>
    [HttpPost]
    public async Task<IActionResult> CompleteAsync([FromBody] AiResultRequest request, CancellationToken cancellationToken)
    {
        if (request.Stage != "model" || string.IsNullOrWhiteSpace(request.WorkId)
            || string.IsNullOrWhiteSpace(request.GenerationId)
            || (string.IsNullOrWhiteSpace(request.Error) && string.IsNullOrWhiteSpace(request.ModelUrl))
            || request.Error.Length > 1000)
        {
            return BadRequest(new { error = "模型回调参数无效" });
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(request.Error))
            {
                await saveModel.FailAsync(request.WorkId, request.GenerationId, request.Error, cancellationToken);
                return Ok(new { status = "failed" });
            }

            await saveModel.CompleteAsync(request.WorkId, request.GenerationId, request.ModelUrl, cancellationToken);
            return Ok(new { status = "ok" });
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { error = "模型任务已失效或重复提交" });
        }
        catch (InvalidDataException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "上游模型文件不符合 GLB 格式或大小限制" });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "下载模型文件失败" });
        }
    }
}
