using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Api.ViewModels.Ai;
using Tongyin.Application.Ai;

namespace Tongyin.Api.Controllers.Admin;

/// <summary>运营配置图片 Seedream 与 3D Worker API，读取不泄露服务商密钥。</summary>
[ApiController]
[AdminAuthorize]
[Route("api/admin/ai/settings")]
public sealed class AdminAiSettingsController(ManageAiStageSettings settings) : ControllerBase
{
    /// <summary>只读返回选中适配器、提示词、启用状态与已配置密钥标记。</summary>
    [HttpGet("{stage}")]
    public async Task<IActionResult> GetAsync(string stage, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await settings.GetAsync(stage, cancellationToken));
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { error = error.Message });
        }
    }

    /// <summary>验证安全 HTTPS 端点并保存单阶段默认服务商、密钥和提示词。</summary>
    [HttpPut("{stage}")]
    public async Task<IActionResult> UpdateAsync(string stage, [FromBody] UpdateAiStageSettingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await settings.SaveAsync(stage, request.ToCommand(), cancellationToken));
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { error = error.Message });
        }
        catch (InvalidOperationException error)
        {
            return Conflict(new { error = error.Message });
        }
    }
}
