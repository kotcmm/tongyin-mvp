using System.Security.Cryptography;
using System.Text;

namespace Tongyin.Infrastructure.Ai;

/// <summary>
/// 兼容 Go 后台 SHA-256 派生密钥与 AES-256-GCM 密文格式的 API Key 解密器。
/// </summary>
public sealed class GoAiKeyDecryptor(string sessionSecret)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _sessionSecret = string.IsNullOrWhiteSpace(sessionSecret)
        ? throw new ArgumentException("SESSION_SECRET 不能为空", nameof(sessionSecret))
        : sessionSecret;

    /// <summary>为新管理员配置使用与历史 Go 完全兼容的 AES-256-GCM 密文；不存储明文。</summary>
    public string Encrypt(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API Key 不能为空", nameof(apiKey));
        }

        var key = SHA256.HashData(Encoding.UTF8.GetBytes("tongyin-ai-api-key-v1:" + _sessionSecret));
        var plaintext = Encoding.UTF8.GetBytes(apiKey);
        var result = new byte[NonceSize + plaintext.Length + TagSize];
        RandomNumberGenerator.Fill(result.AsSpan(0, NonceSize));
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(result.AsSpan(0, NonceSize), plaintext,
                result.AsSpan(NonceSize, plaintext.Length), result.AsSpan(NonceSize + plaintext.Length, TagSize));
            return Convert.ToBase64String(result).TrimEnd('=');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(result);
        }
    }

    /// <summary>
    /// 解密 Go RawStdEncoding 编码的 nonce、密文和认证标签；不暴露密钥或原始密文。
    /// </summary>
    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            throw new InvalidOperationException("尚未配置 API Key");
        }

        var payload = DecodePayload(ciphertext);
        if (payload.Length < NonceSize + TagSize)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidOperationException("密钥存储损坏");
        }

        var key = SHA256.HashData(Encoding.UTF8.GetBytes("tongyin-ai-api-key-v1:" + _sessionSecret));
        var plaintext = new byte[payload.Length - NonceSize - TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                payload.AsSpan(0, NonceSize),
                payload.AsSpan(NonceSize, plaintext.Length),
                payload.AsSpan(NonceSize + plaintext.Length, TagSize),
                plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("无法解密密钥：请检查 SESSION_SECRET 是否变化");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static byte[] DecodePayload(string ciphertext)
    {
        try
        {
            // Go base64.RawStdEncoding 不带 '='，.NET 需要补全填充。
            var padding = (4 - ciphertext.Length % 4) % 4;
            return Convert.FromBase64String(ciphertext + new string('=', padding));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("API Key 密文格式无效");
        }
    }
}
