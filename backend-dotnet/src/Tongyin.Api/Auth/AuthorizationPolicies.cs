namespace Tongyin.Api.Auth;

/// <summary>
/// 集中管理各类身份使用的授权策略名称，避免将不同登录体系混为一谈。
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// 微信小程序用户：必须持有有效微信会话令牌，且数据库账号处于可用状态。
    /// </summary>
    public const string Wechat = "Wechat";

    /// <summary>
    /// 管理员：必须通过独立的管理员令牌认证，不接受微信用户会话替代。
    /// </summary>
    public const string Admin = "Admin";

    /// <summary>AI worker 结果回调：使用与微信、管理员完全独立的凭据验证。</summary>
    public const string AiWebhook = "AiWebhook";
}
