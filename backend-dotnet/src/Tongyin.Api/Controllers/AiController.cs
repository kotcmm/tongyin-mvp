using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Application;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
[ApiController]
public sealed class AiController(AiManagementService service) : ControllerBase
{
    [HttpGet("/api/admin/ai/config")]
    public Task<IResult> GetAiConfig() => service.GetAiConfig(HttpContext);

    [HttpPost("/api/admin/ai/providers")]
    public Task<IResult> CreateProvider() => service.CreateProvider(HttpContext);

    [HttpDelete("/api/admin/ai/providers/{id}")]
    public Task<IResult> DeleteProvider([FromRoute] string id) => service.DeleteProvider(id, HttpContext);

    [HttpPut("/api/admin/ai/prompts/{stage}")]
    public Task<IResult> UpdatePrompt([FromRoute] string stage) => service.UpdatePrompt(stage, HttpContext);

    [HttpPost("/api/internal/ai-result")]
    public Task<IResult> HandleWorkerCallback() => service.HandleWorkerCallback(HttpContext);
}
