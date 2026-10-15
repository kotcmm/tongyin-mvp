using System.Security.Cryptography;
using System.Text;

namespace Tongyin.Infrastructure.Ai;

/// <summary>
/// 解密 Go 运营后台保存的 AI API Key，兼容 SHA-256 派生密钥与 AES-256-GCM 密文格式。
/// </summary>
public sealed class GoAiKeyDecryptor(string sessionSecret)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly string _sessionSecret = string.IsNullOrWhiteSpace(sessionSecret)
        ? throw new ArgumentException("SESSION_SECRET 不能为空", nameof(sessionSecret))
        : sessionSecret;

    /// <summary>
    /// 解密 base64.RawStdEncoding 保存的 nonce、密文及认证标签；不会暴露原始密文或密钥。
    /// </summary>
    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            throw new InvalidOperationException("尚未配置 API Key");
        }

        byte[] data;
        try
        {
            // Go RawStdEncoding 不带 '='，.NET FromBase64String 需要补齐填充。
            var padding = (4 - ciphertext.Length % 4) % 4;
            data = Convert.FromBase64String(ciphertext + new string('=', padding));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("API Key 密文格式无效");
        }

        if (data.Length < NonceSize + TagSize)
        {
            CryptographicOperations.ZeroMemory(data);
            throw new InvalidOperationException("密钥存储损坏");
        }

        var key = SHA256.HashData(Encoding.UTF8.GetBytes("tongyin-ai-api-key-v1:" + _sessionSecret));
        var plaintext = new byte[data.Length - NonceSize - TagSize];

        try
        {
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(
                    data.AsSpan(0, NonceSize),
                    data.AsSpan(NonceSize, plaintext.Length),
                    data.AsSpan(NonceSize + plaintext.Length, TagSize),
                    plaintext);
            }

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
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
