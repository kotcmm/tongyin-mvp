using Microsoft.AspNetCore.Authorization;

namespace Tongyin.Api.Auth;

/// <summary>
/// 微信登录用户的专用授权标记，使用微信认证方案并校验账号状态。
/// 不适用于管理后台接口。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class WechatAuthorizeAttribute : AuthorizeAttribute
{
    /// <summary>
    /// 将微信用户专用授权策略绑定到控制器或操作方法。
    /// </summary>
    public WechatAuthorizeAttribute()
    {
        Policy = AuthorizationPolicies.Wechat;
    }
}
