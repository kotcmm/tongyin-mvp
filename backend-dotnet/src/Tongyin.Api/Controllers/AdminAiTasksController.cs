using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Application.Works;

namespace Tongyin.Api.Controllers;

/// <summary>
/// 管理员只读 AI 任务核查入口，使用已有 Go 后台的 X-Admin-Token 认证约定。
/// </summary>
[ApiController]
[Route("api/admin/ai")]
[AdminAuthorize]
public sealed class AdminAiTasksController(ListStalledImageTasks listTasks) : ControllerBase
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
}
