using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tongyin.Infrastructure.Auth;

/// <summary>
/// 校验原 Go 后端使用的 HMAC-SHA256 微信登录会话令牌，兼容现有小程序登录态。
/// </summary>
public sealed class GoSessionTokenValidator
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);
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

        if (token.Split('.') is not [var payload, var signature])
        {
            return false;
        }

        if (!HasValidSignature(payload, signature))
        {
            return false;
        }

        if (!TryParseSession(payload, out var owner, out var expiresAt))
        {
            return false;
        }

        if (now.ToUnixTimeSeconds() > expiresAt)
        {
            return false;
        }

        openId = owner;
        return true;
    }

    private bool HasValidSignature(string payload, string signature)
    {
        if (!TryDecodeBase64Url(signature, out var actual))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(payload));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static bool TryParseSession(string payload, out string owner, out long expiresAt)
    {
        owner = string.Empty;
        expiresAt = 0;
        if (!TryDecodeBase64Url(payload, out var data))
        {
            return false;
        }

        string text;
        try
        {
            text = _strictUtf8.GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var fields = text.Split('|');
        if (fields.Length != 2 || fields[0].Length == 0
            || !long.TryParse(fields[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out expiresAt))
        {
            return false;
        }

        owner = fields[0];
        return true;
    }

    private static bool TryDecodeBase64Url(string encoded, out byte[] decoded)
    {
        decoded = Array.Empty<byte>();
        if (encoded.Length == 0 || encoded.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')))
        {
            return false;
        }

        var buffer = new byte[encoded.Length];
        try
        {
            if (!Base64Url.TryDecodeFromChars(encoded, buffer, out var length))
            {
                return false;
            }

            decoded = buffer[..length];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
