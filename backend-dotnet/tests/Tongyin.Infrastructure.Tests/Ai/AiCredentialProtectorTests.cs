using Microsoft.AspNetCore.DataProtection;
using Tongyin.Infrastructure.Ai;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Ai;

/// <summary>标准 Data Protection 服务商 API Key 加解密；不保留 Go 特有密文兼容性。</summary>
public sealed class AiCredentialProtectorTests
{
    /// <summary>相同明文被随机加密，无法从配置表直接读取。</summary>
    [Fact]
    public void Protect_UsesVersionedAuthenticatedEncryption()
    {
        using var folder = new TempDir();
        var protector = new AiCredentialProtector(DataProtectionProvider.Create(new DirectoryInfo(folder.Path)));
        var a = protector.Encrypt("sk-example-secret");
        var b = protector.Encrypt("sk-example-secret");
        Assert.NotEqual(a, b);
        Assert.StartsWith("dp1:", a);
        Assert.Equal("sk-example-secret", protector.Decrypt(a));
        Assert.Equal("sk-example-secret", protector.Decrypt(b));
    }

    /// <summary>旧的明文/其他格式不能被当成有效密钥，要求重新配置。</summary>
    [Fact]
    public void OldCipher_IsRejected()
    {
        using var folder = new TempDir();
        var protector = new AiCredentialProtector(DataProtectionProvider.Create(new DirectoryInfo(folder.Path)));
        Assert.Throws<InvalidOperationException>(() => protector.Decrypt("legacy-cipher"));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tongyin-protector-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
