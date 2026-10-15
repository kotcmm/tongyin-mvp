using System.Net;
using Tongyin.Infrastructure.Ai;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Ai;

/// <summary>
/// 下载生成图片时，DNS 连接目标只能是公网地址，阻止域名解析到内网服务。
/// </summary>
public sealed class PublicImageNetworkPolicyTests
{
    /// <summary>
    /// 常见回环、内网、链路本地及云元数据地址必须拒绝。
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void IsPublicAddress_InternalIp_IsRejected(string ip)
    {
        Assert.False(PublicImageNetworkPolicy.IsPublicAddress(IPAddress.Parse(ip)));
    }

    /// <summary>
    /// 公网 IPv4 / IPv6 地址允许作为下载连接的候选目标。
    /// </summary>
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void IsPublicAddress_GlobalIp_IsAllowed(string ip)
    {
        Assert.True(PublicImageNetworkPolicy.IsPublicAddress(IPAddress.Parse(ip)));
    }
}
