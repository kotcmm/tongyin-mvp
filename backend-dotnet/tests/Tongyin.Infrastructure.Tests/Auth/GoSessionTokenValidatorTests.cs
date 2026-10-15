using System.Security.Cryptography;
using System.Text;
using Tongyin.Infrastructure.Auth;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Auth;

/// <summary>
/// 验证新后端可读取原 Go 服务签发的微信登录会话令牌。
/// </summary>
public sealed class GoSessionTokenValidatorTests
{
    private const string Secret = "test-session-secret-longer-than-24-chars";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1792065600);

    /// <summary>
    /// 有效签名和未过期的旧版令牌能解析出登录用户的 OpenID。
    /// </summary>
    [Fact]
    public void TryValidate_ValidGoToken_ReturnsOwner()
    {
        var validator = new GoSessionTokenValidator(Secret);
        var token = Issue("openid-001", Now.AddHours(1).ToUnixTimeSeconds());

        var valid = validator.TryValidate(token, Now, out var openId);

        Assert.True(valid);
        Assert.Equal("openid-001", openId);
    }

    /// <summary>
    /// 超过令牌到期秒数后不能再通过登录验证。
    /// </summary>
    [Fact]
    public void TryValidate_ExpiredToken_Rejects()
    {
        var validator = new GoSessionTokenValidator(Secret);
        var token = Issue("openid-001", Now.AddSeconds(-1).ToUnixTimeSeconds());

        Assert.False(validator.TryValidate(token, Now, out var openId));
        Assert.Equal(string.Empty, openId);
    }

    /// <summary>
    /// 与原 Go 后端保持一致：到期秒数本身仍视为有效。
    /// </summary>
    [Fact]
    public void TryValidate_AtExpiryBoundary_Accepts()
    {
        var validator = new GoSessionTokenValidator(Secret);
        var token = Issue("openid-001", Now.ToUnixTimeSeconds());

        Assert.True(validator.TryValidate(token, Now, out var openId));
        Assert.Equal("openid-001", openId);
    }

    /// <summary>
    /// 修改令牌内容却不重新签名时必须拒绝。
    /// </summary>
    [Fact]
    public void TryValidate_TamperedPayload_Rejects()
    {
        var validator = new GoSessionTokenValidator(Secret);
        var token = Issue("openid-001", Now.AddHours(1).ToUnixTimeSeconds());
        var dot = token.IndexOf('.');
        var forgedPayload = Base64Url(Encoding.UTF8.GetBytes($"other-user|{Now.AddHours(1).ToUnixTimeSeconds()}"));
        var tampered = forgedPayload + token[dot..];

        Assert.False(validator.TryValidate(tampered, Now, out var openId));
        Assert.Equal(string.Empty, openId);
    }

    /// <summary>
    /// 错误服务器密钥签发的令牌不能登录。
    /// </summary>
    [Fact]
    public void TryValidate_WrongSigningSecret_Rejects()
    {
        var validator = new GoSessionTokenValidator("another-session-secret");
        var token = Issue("openid-001", Now.AddHours(1).ToUnixTimeSeconds());

        Assert.False(validator.TryValidate(token, Now, out _));
    }

    /// <summary>
    /// 不完整、缺少签名或包含多个分隔点的令牌不能被接受。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("broken")]
    [InlineData("a.")]
    [InlineData(".a")]
    [InlineData("a.b.c")]
    [InlineData("**.**")]
    public void TryValidate_MalformedToken_Rejects(string token)
    {
        var validator = new GoSessionTokenValidator(Secret);

        Assert.False(validator.TryValidate(token, Now, out var openId));
        Assert.Equal(string.Empty, openId);
    }

    /// <summary>
    /// 签名正确但用户 OpenID 为空时不得认证成功。
    /// </summary>
    [Fact]
    public void TryValidate_EmptyOwner_Rejects()
    {
        var validator = new GoSessionTokenValidator(Secret);
        var token = Issue(string.Empty, Now.AddHours(1).ToUnixTimeSeconds());

        Assert.False(validator.TryValidate(token, Now, out _));
    }

    /// <summary>
    /// 缺失会话密钥时不得创建默认可用的验证器。
    /// </summary>
    [Fact]
    public void Constructor_EmptySecret_Rejects()
    {
        Assert.Throws<ArgumentException>(() => new GoSessionTokenValidator(string.Empty));
    }

    /// <summary>
    /// 原 Go 的 RawURLEncoding 不接受 Base64Url 中的空格，即使解码结果本身相同。
    /// </summary>
    [Fact]
    public void TryValidate_SignatureContainsWhitespace_Rejects()
    {
        var validator = new GoSessionTokenValidator(Secret);
        var token = Issue("openid-001", Now.AddHours(1).ToUnixTimeSeconds());
        var separator = token.IndexOf('.');
        var tokenWithWhitespace = token.Insert(separator + 4, " ");

        Assert.False(validator.TryValidate(tokenWithWhitespace, Now, out var openId));
        Assert.Equal(string.Empty, openId);
    }

    /// <summary>
    /// 签名正确也不能绕过载荷字段及过期秒数字段的格式校验。
    /// </summary>
    [Theory]
    [InlineData("openid-001")]
    [InlineData("|1792070000")]
    [InlineData("openid-001|invalid")]
    [InlineData("openid-001|1792070000|extra")]
    public void TryValidate_SignedMalformedPayload_Rejects(string payload)
    {
        var validator = new GoSessionTokenValidator(Secret);
        var token = SignPayload(Base64Url(Encoding.UTF8.GetBytes(payload)));

        Assert.False(validator.TryValidate(token, Now, out var openId));
        Assert.Equal(string.Empty, openId);
    }

    /// <summary>
    /// 不合法的 UTF-8 编码即使签名正确，也不能生成有效的微信用户标识。
    /// </summary>
    [Fact]
    public void TryValidate_SignedInvalidUtf8Payload_Rejects()
    {
        var validator = new GoSessionTokenValidator(Secret);
        var token = SignPayload(Base64Url([0xFF, 0xFE, 0xFD]));

        Assert.False(validator.TryValidate(token, Now, out var openId));
        Assert.Equal(string.Empty, openId);
    }

    /// <summary>
    /// 原 Go 使用无填充的 Base64Url，不能接收带填充符的签名。
    /// </summary>
    [Fact]
    public void TryValidate_PaddedSignature_Rejects()
    {
        var validator = new GoSessionTokenValidator(Secret);
        var token = Issue("openid-001", Now.AddHours(1).ToUnixTimeSeconds()) + "=";

        Assert.False(validator.TryValidate(token, Now, out var openId));
        Assert.Equal(string.Empty, openId);
    }

    private static string Issue(string openId, long expiresAt)
    {
        var payload = Base64Url(Encoding.UTF8.GetBytes($"{openId}|{expiresAt}"));
        return SignPayload(payload);
    }

    private static string SignPayload(string encodedPayload)
    {
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(encodedPayload));
        return encodedPayload + "." + Base64Url(signature);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
