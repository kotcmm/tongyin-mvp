using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Application.Auth;

namespace Tongyin.Api.Controllers;

/// <summary>微信小程序用户个人信息接口，身份从认证 Claims 中读取。</summary>
[ApiController]
[Route("api/me")]
[WechatAuthorize]
public sealed class WechatProfileController(GetWechatProfile getWechatProfile) : ControllerBase
{
    /// <summary>返回当前登录用户的内部 ID、注册时间及作品订单数量，不暴露 OpenID。</summary>
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var profile = await getWechatProfile.ExecuteAsync(User.GetWechatOpenId(), cancellationToken);
        if (profile is null)
        {
            return NotFound(new { error = "用户信息不可用" });
        }

        return Ok(new
        {
            id = profile.Id,
            created_at = profile.CreatedAt,
            works = profile.Works,
            orders = profile.Orders
        });
    }
}
