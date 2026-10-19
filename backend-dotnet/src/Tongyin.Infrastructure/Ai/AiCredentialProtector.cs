using Microsoft.AspNetCore.DataProtection;

namespace Tongyin.Infrastructure.Ai;

/// <summary>使用 ASP.NET Core Data Protection 加密 AI 服务商密钥；不依赖 Go 或自写密码协议。</summary>
public sealed class AiCredentialProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Tongyin.Ai.ApiKey.v1");

    /// <summary>加密新密钥；密文带版本前缀，避免误用旧不可解密的存储格式。</summary>
    public string Encrypt(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("API Key 不得为空", nameof(key));
        return "dp1:" + _protector.Protect(key);
    }

    /// <summary>解密当前 Identity/Data Protection 体系的密钥；旧格式必须在管理后台重新设置。</summary>
    public string Decrypt(string ciphertext)
    {
        if (!IsCurrentCipher(ciphertext)) throw new InvalidOperationException("请在后台重新设置该服务商的 API Key");
        try { return _protector.Unprotect(ciphertext[4..]); }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new InvalidOperationException("服务商 API Key 已失效，请在后台重新设置");
        }
    }

    /// <summary>判断密文是否由当前 .NET 服务写入。</summary>
    public static bool IsCurrentCipher(string? value) => value?.StartsWith("dp1:", StringComparison.Ordinal) == true;
}
