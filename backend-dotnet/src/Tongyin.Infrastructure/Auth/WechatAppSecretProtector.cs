using System.Security.Cryptography;
using System.Text;

namespace Tongyin.Infrastructure.Auth;

/// <summary>为微信 AppSecret 派生独立 AES-256-GCM 加密密钥，防止数据库存储明文。</summary>
public sealed class WechatAppSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    /// <summary>使用现有服务端主密钥派生微信凭据用途的独立密钥。</summary>
    public WechatAppSecretProtector(string sessionSecret)
    {
        if (string.IsNullOrWhiteSpace(sessionSecret))
        {
            throw new ArgumentException("缺少服务端密钥", nameof(sessionSecret));
        }

        _key = SHA256.HashData(Encoding.UTF8.GetBytes("tongyin-wechat-app-secret-v1:" + sessionSecret));
    }

    /// <summary>使用随机 nonce 加密凭据，输出标准 Base64 存储文本。</summary>
    public string Encrypt(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("AppSecret 不能为空", nameof(secret));
        }

        var plaintext = Encoding.UTF8.GetBytes(secret);
        var encrypted = new byte[NonceSize + plaintext.Length + TagSize];
        RandomNumberGenerator.Fill(encrypted.AsSpan(0, NonceSize));
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(encrypted.AsSpan(0, NonceSize), plaintext,
                encrypted.AsSpan(NonceSize, plaintext.Length),
                encrypted.AsSpan(NonceSize + plaintext.Length, TagSize));
            return Convert.ToBase64String(encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>解密数据库密文，认证标签无效时拒绝返回凭据。</summary>
    public string Decrypt(string ciphertext)
    {
        var encrypted = Convert.FromBase64String(ciphertext);
        if (encrypted.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("微信应用密钥密文无效");
        }

        var plaintext = new byte[encrypted.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(encrypted.AsSpan(0, NonceSize),
                encrypted.AsSpan(NonceSize, plaintext.Length),
                encrypted.AsSpan(NonceSize + plaintext.Length, TagSize), plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }
}
