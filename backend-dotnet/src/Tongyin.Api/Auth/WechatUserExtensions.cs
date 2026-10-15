using System.Security.Claims;

namespace Tongyin.Api.Auth;

/// <summary>
/// 微信用户身份的便捷访问方法，区分微信认证与其他登录渠道。
/// </summary>
public static class WechatUserExtensions
{
    /// <summary>
    /// 从已认证的微信会话身份读取 OpenID；缺少有效微信身份时抛出配置异常。
    /// </summary>
    public static string GetWechatOpenId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = user.Identities.FirstOrDefault(identity =>
            identity.IsAuthenticated &&
            string.Equals(identity.AuthenticationType, WechatSessionAuthenticationHandler.SchemeName, StringComparison.Ordinal));
        var openId = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(openId))
        {
            throw new InvalidOperationException("当前请求缺少已认证的微信 OpenID");
        }

        return openId;
    }
}
