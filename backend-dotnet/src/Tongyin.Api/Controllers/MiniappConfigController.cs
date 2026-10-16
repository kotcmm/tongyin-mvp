using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Services;
using Tongyin.Application.Ai;

namespace Tongyin.Api.Controllers;

/// <summary>小程序可公开读取的业务配置，不包含模型 API Key 和其他服务端秘密。</summary>
[ApiController]
[Route("api/config")]
[AllowAnonymous]
public sealed class MiniappConfigController(MiniappBusinessSettings settings, IAiGenerationSettingsReader aiSettingsReader, IHostEnvironment environment, IConfiguration configuration) : ControllerBase
{
    /// <summary>返回价格、开发模式、图片生成可用状态与免费次数。</summary>
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var imageSettings = await aiSettingsReader.GetAsync("image", cancellationToken);
        return Ok(new
        {
            price_fen = settings.PriceFen,
            dev = environment.IsDevelopment() && configuration.GetValue<bool>("DEV_MODE"),
            ai_auto_enabled = imageSettings.CanGenerateImage(),
            free_image_attempts = settings.FreeImageAttempts
        });
    }
}
