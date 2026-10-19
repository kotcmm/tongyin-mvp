using Tongyin.Infrastructure.Ai;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Ai;

/// <summary>
/// 验证 .NET 解密器兼容 Go 运营后台 aiEncrypt 生成的 AES-GCM 密文。
/// </summary>
public sealed class GoAiKeyDecryptorTests
{
    private const string SessionSecret = "local-test-session-secret-with-32bytes";
    private const string PlainApiKey = "sk-test-中文-AI-密钥";

    // 由 Go crypto/aes + cipher.NewGCM + base64.RawStdEncoding 用固定 nonce [0..11] 生成。
    // 格式：nonce(12) + ciphertext(UTF-8 明文) + tag(16)；仅为公开测试数据。
    private const string GoCiphertext = "AAECAwQFBgcICQoLFkRZbdrMirsVfEoRLZOOtxWFtmeIMysZKY0/SkPmYje9JP1rZcI8yw";

    /// <summary>
    /// .NET 必须能解密原 Go 格式的无填充标准 Base64 密文，包括中文 UTF-8。
    /// </summary>
    [Fact]
    public void Decrypt_GoAesGcmVector_ReturnsOriginalUtf8Key()
    {
        var decryptor = new GoAiKeyDecryptor(SessionSecret);

        Assert.Equal(PlainApiKey, decryptor.Decrypt(GoCiphertext));
    }

    /// <summary>
    /// SESSION_SECRET 不一致时认证必须失败，不可返回乱码或错误密钥。
    /// </summary>
    [Fact]
    public void Decrypt_DifferentSessionSecret_Rejects()
    {
        var decryptor = new GoAiKeyDecryptor("a-different-secret-with-sufficient-length");

        var exception = Assert.Throws<InvalidOperationException>(() => decryptor.Decrypt(GoCiphertext));

        Assert.DoesNotContain(PlainApiKey, exception.Message);
        Assert.Contains("SESSION_SECRET", exception.Message);
    }

    /// <summary>
    /// 篡改认证标签即使 Base64 语法仍合法也必须拒绝，不能忽略 GCM 完整性验证。
    /// </summary>
    [Fact]
    public void Decrypt_ModifiedAuthenticationTag_Rejects()
    {
        var data = Convert.FromBase64String(GoCiphertext + new string('=', (4 - GoCiphertext.Length % 4) % 4));
        data[^1] ^= 0x01;
        var modified = Convert.ToBase64String(data).TrimEnd('=');
        var decryptor = new GoAiKeyDecryptor(SessionSecret);

        Assert.Throws<InvalidOperationException>(() => decryptor.Decrypt(modified));
    }

    /// <summary>
    /// 损坏的 Base64 密文必须转换为明确的配置错误，不能泄露原始密文。
    /// </summary>
    [Fact]
    public void Decrypt_InvalidBase64_Rejects()
    {
        var decryptor = new GoAiKeyDecryptor(SessionSecret);

        var exception = Assert.Throws<InvalidOperationException>(() => decryptor.Decrypt("not@base64!"));

        Assert.DoesNotContain("not@base64!", exception.Message);
    }

    /// <summary>
    /// 尚未配置 Key 时给出可识别的错误，而不是尝试执行 AES-GCM。
    /// </summary>
    [Fact]
    public void Decrypt_MissingCiphertext_Rejects()
    {
        var decryptor = new GoAiKeyDecryptor(SessionSecret);

        var exception = Assert.Throws<InvalidOperationException>(() => decryptor.Decrypt(string.Empty));

        Assert.Contains("API Key", exception.Message);
    }

    /// <summary>
    /// nonce 和 GCM 认证标签长度不足时明确拒绝损坏数据。
    /// </summary>
    [Fact]
    public void Decrypt_TruncatedData_Rejects()
    {
        var decryptor = new GoAiKeyDecryptor(SessionSecret);
        var truncated = Convert.ToBase64String(new byte[27]).TrimEnd('=');

        Assert.Throws<InvalidOperationException>(() => decryptor.Decrypt(truncated));
    }

    /// <summary>运营后台保存的随机加密 API Key 必须可按旧 Go 格式解密，且不能明文入库。</summary>
    [Fact]
    public void Encrypt_RoundTrip_UsesCompatibleEncryptedPayload()
    {
        var protector = new GoAiKeyDecryptor(SessionSecret);
        var first = protector.Encrypt(PlainApiKey);
        var second = protector.Encrypt(PlainApiKey);

        Assert.NotEqual(first, second);
        Assert.DoesNotContain(PlainApiKey, first);
        Assert.Equal(PlainApiKey, protector.Decrypt(first));
        Assert.Equal(PlainApiKey, protector.Decrypt(second));
    }

    /// <summary>
    /// 空会话密钥不可用于解密现有后台配置。
    /// </summary>
    [Fact]
    public void Constructor_EmptySessionSecret_Rejects()
    {
        Assert.Throws<ArgumentException>(() => new GoAiKeyDecryptor(string.Empty));
    }
}
