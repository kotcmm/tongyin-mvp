using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tongyin.Infrastructure.Auth;

/// <summary>
/// 校验原 Go 后端使用的 HMAC-SHA256 微信登录会话令牌，兼容现有小程序登录态。
/// </summary>
public sealed class GoSessionTokenValidator
{
    private readonly byte[] _secret;

    /// <summary>
    /// 使用 Go 后端一致的 SESSION_SECRET 创建令牌验证器，禁止空密钥。
    /// </summary>
    public GoSessionTokenValidator(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("会话密钥不能为空", nameof(secret));
        }

        _secret = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>
    /// 验证令牌签名和到期时间，成功后返回所属微信用户 OpenID。
    /// </summary>
    public bool TryValidate(string token, DateTimeOffset now, out string openId)
    {
        openId = string.Empty;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot != token.LastIndexOf('.') || dot == token.Length - 1)
        {
            return false;
        }

        var encodedPayload = token[..dot];
        if (!TryBase64UrlDecode(token[(dot + 1)..], out var signature))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(encodedPayload));
        if (!CryptographicOperations.FixedTimeEquals(expected, signature)
            || !TryBase64UrlDecode(encodedPayload, out var payload))
        {
            return false;
        }

        string value;
        try
        {
            value = new UTF8Encoding(false, true).GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var separator = value.IndexOf('|');
        if (separator <= 0 || separator != value.LastIndexOf('|'))
        {
            return false;
        }

        if (!long.TryParse(value[(separator + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var expiresAt)
            || now.ToUnixTimeSeconds() > expiresAt)
        {
            return false;
        }

        openId = value[..separator];
        return true;
    }

    private static bool TryBase64UrlDecode(string encoded, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (encoded.Length == 0 || encoded.Length % 4 == 1
            || encoded.IndexOfAny(['+', '/', '=']) >= 0)
        {
            return false;
        }

        var base64 = encoded.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
