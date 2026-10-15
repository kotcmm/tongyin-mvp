using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Tongyin.Infrastructure.Auth;

namespace Tongyin.Api.Auth;

/// <summary>
/// 在统一授权管线检查微信用户是否存在且未被停用。
/// </summary>
public sealed class EnabledWechatUserAuthorizationHandler : AuthorizationHandler<EnabledWechatUserRequirement>
{
    private readonly SqlUserAccess _userAccess;

    /// <summary>
    /// 使用现有的用户数据读取能力校验账号状态。
    /// </summary>
    public EnabledWechatUserAuthorizationHandler(SqlUserAccess userAccess)
    {
        _userAccess = userAccess;
    }

    /// <summary>
    /// 仅当用户有效且可用时通过授权要求。
    /// </summary>
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, EnabledWechatUserRequirement requirement)
    {
        var openId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(openId))
        {
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (await _userAccess.IsEnabledAsync(openId, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}
