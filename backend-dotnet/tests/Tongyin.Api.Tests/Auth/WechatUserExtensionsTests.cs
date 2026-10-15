using System.Security.Claims;
using Tongyin.Api.Auth;
using Xunit;

namespace Tongyin.Api.Tests.Auth;

/// <summary>
/// 验证从已认证的微信身份中安全、统一地获取微信 OpenID。
/// </summary>
public sealed class WechatUserExtensionsTests
{
    /// <summary>
    /// 微信认证成功后，返回身份处理器写入的 OpenID。
    /// </summary>
    [Fact]
    public void GetWechatOpenId_AuthenticatedWechatIdentity_ReturnsOpenId()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "openid-001")],
            WechatSessionAuthenticationHandler.SchemeName));

        Assert.Equal("openid-001", user.GetWechatOpenId());
    }

    /// <summary>
    /// 微信身份缺少 OpenID 时明确拒绝，不得使用空值或强制忽略空值警告。
    /// </summary>
    [Fact]
    public void GetWechatOpenId_MissingOpenId_Throws()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            Array.Empty<Claim>(),
            WechatSessionAuthenticationHandler.SchemeName));

        Assert.Throws<InvalidOperationException>(() => user.GetWechatOpenId());
    }

    /// <summary>
    /// 其他渠道即使含有同名 NameIdentifier，也不能作为微信 OpenID。
    /// </summary>
    [Fact]
    public void GetWechatOpenId_NonWechatAuthentication_Throws()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "alipay-user-001")],
            "AlipaySession"));

        Assert.Throws<InvalidOperationException>(() => user.GetWechatOpenId());
    }

    /// <summary>
    /// 多认证身份并存时，只读取微信认证方案的 OpenID。
    /// </summary>
    [Fact]
    public void GetWechatOpenId_MultipleIdentities_SelectsWechat()
    {
        var user = new ClaimsPrincipal(
        [
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "admin-001")], "AdminSession"),
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "openid-001")],
                WechatSessionAuthenticationHandler.SchemeName)
        ]);

        Assert.Equal("openid-001", user.GetWechatOpenId());
    }

    /// <summary>
    /// 未经认证的 Claims 不构成微信登录态。
    /// </summary>
    [Fact]
    public void GetWechatOpenId_UnauthenticatedIdentity_Throws()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "openid-001")]));

        Assert.Throws<InvalidOperationException>(() => user.GetWechatOpenId());
    }
}
