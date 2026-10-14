using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
/// <summary>
/// 人工智能服务配置和任务回调接口。
/// </summary>
[ApiController]
public sealed class AiController(AiManagementService service) : ControllerBase
{
    /// <summary>
    /// 读取人工智能模型与提示词配置。
    /// </summary>
    [HttpGet("/api/admin/ai/config")]
    public Task<IResult> GetAiConfig() => service.GetAiConfig(HttpContext);

    /// <summary>
    /// 新增人工智能模型服务商配置。
    /// </summary>
    [HttpPost("/api/admin/ai/providers")]
    public Task<IResult> CreateProvider() => service.CreateProvider(HttpContext);

    /// <summary>
    /// 删除人工智能模型服务商配置。
    /// </summary>
    [HttpDelete("/api/admin/ai/providers/{id}")]
    public Task<IResult> DeleteProvider([FromRoute] string id) => service.DeleteProvider(id, HttpContext);

    /// <summary>
    /// 更新指定生成阶段的提示词。
    /// </summary>
    [HttpPut("/api/admin/ai/prompts/{stage}")]
    public Task<IResult> UpdatePrompt([FromRoute] string stage) => service.UpdatePrompt(stage, HttpContext);

    /// <summary>
    /// 接收并处理后台生成任务结果。
    /// </summary>
    [HttpPost("/api/internal/ai-result")]
    public Task<IResult> HandleWorkerCallback() => service.HandleWorkerCallback(HttpContext);
}
