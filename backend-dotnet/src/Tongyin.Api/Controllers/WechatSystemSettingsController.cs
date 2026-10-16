using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Api.ViewModels.Auth;
using Tongyin.Application.Auth;

namespace Tongyin.Api.Controllers;

/// <summary>管理员专用微信小程序凭据配置接口，绝不返回明文 AppSecret。</summary>
[ApiController]
[Route("api/admin/system/wechat")]
[AdminAuthorize]
public sealed class WechatSystemSettingsController(ManageWechatAppSettings settings) : ControllerBase
{
    /// <summary>仅返回 AppID 和是否配置密钥。</summary>
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var status = await settings.ReadStatusAsync(cancellationToken);
        return Ok(new { app_id = status.AppId, has_app_secret = status.HasAppSecret });
    }

    /// <summary>修改 AppID 和可选的新密钥；空密钥表示不修改原有密钥。</summary>
    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] UpdateWechatAppSettingsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var status = await settings.SaveAsync(request.AppId, request.AppSecret, cancellationToken);
            return Ok(new { app_id = status.AppId, has_app_secret = status.HasAppSecret });
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { error = error.Message });
        }
    }
}
