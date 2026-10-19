using Microsoft.AspNetCore.Authorization;

namespace Tongyin.Api.Auth;

/// <summary>
/// 管理员接口的专用授权标记，与微信用户认证严格分离。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AdminAuthorizeAttribute : AuthorizeAttribute
{
    /// <summary>
    /// 通过 Admin 授权策略启用管理员登录会话认证。
    /// </summary>
    public AdminAuthorizeAttribute()
    {
        Policy = AuthorizationPolicies.Admin;
    }
}
