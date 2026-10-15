using Microsoft.AspNetCore.Authorization;

namespace Tongyin.Api.Auth;

/// <summary>
/// 微信小程序用户的专用授权标记，沿用现有客户策略并校验登录及账号状态。
/// 不适用于管理后台接口。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CustomerAuthorizeAttribute : AuthorizeAttribute
{
    /// <summary>
    /// 将微信用户专用授权策略绑定到控制器或操作方法。
    /// </summary>
    public CustomerAuthorizeAttribute()
    {
        Policy = AuthorizationPolicies.Customer;
    }
}
