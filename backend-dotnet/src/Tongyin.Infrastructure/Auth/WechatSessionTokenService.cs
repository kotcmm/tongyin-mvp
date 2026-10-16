using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tongyin.Application.Auth;

namespace Tongyin.Infrastructure.Auth;

/// <summary>
/// 自有微信登录会话签发与验证服务，统一采用带有效期的 HMAC-SHA256 签名。
/// </summary>
public sealed class WechatSessionTokenService : IWechatSessionIssuer
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);
    private readonly byte[] _secret;

    /// <summary>构造会话令牌服务，不允许使用空密钥。</summary>
    public WechatSessionTokenService(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("会话密钥不能为空", nameof(secret));
        }

        _secret = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>签发指定 OpenID 的会话令牌，由服务端控制过期时间。</summary>
    public string Issue(string openId, DateTimeOffset now, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(openId) || openId.Contains('|'))
        {
            throw new ArgumentException("OpenID 无效", nameof(openId));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        var expiresAt = now.Add(lifetime).ToUnixTimeSeconds();
        var payload = EncodeBase64Url(Encoding.UTF8.GetBytes(openId + "|" + expiresAt.ToString(CultureInfo.InvariantCulture)));
        var signature = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(payload));
        return payload + "." + EncodeBase64Url(signature);
    }

    /// <summary>验证令牌签名和到期时间，成功返回微信用户 OpenID。</summary>
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

    private static string EncodeBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
