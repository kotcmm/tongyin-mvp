using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Tongyin.Infrastructure.Payments;

/// <summary>兼容 PEM 私钥、平台 RSA 公钥与微信支付平台证书的加载器。</summary>
internal static class WechatPayKeyLoader
{
    /// <summary>从 PEM 读取商户 RSA 签名私钥，不写入日志。</summary>
    internal static RSA LoadMerchantKey(string path)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(File.ReadAllText(path));
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw new InvalidOperationException("商户 RSA 私钥配置无效");
        }
    }

    /// <summary>从微信支付平台公钥或证书 PEM 中取得 RSA 验签公钥。</summary>
    internal static RSA LoadPlatformKey(string path)
    {
        var pem = File.ReadAllText(path);
        if (pem.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
        {
            using var certificate = X509Certificate2.CreateFromPem(pem);
            return certificate.GetRSAPublicKey() ?? throw new InvalidOperationException("平台证书未包含 RSA 公钥");
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw new InvalidOperationException("微信支付平台验签公钥无效");
        }
    }

    /// <summary>按照微信支付 v3 约定，用 RSA-SHA256/PKCS#1 v1.5 对原始字符串签名。</summary>
    internal static string Sign(RSA rsa, string message)
        => Convert.ToBase64String(rsa.SignData(System.Text.Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    /// <summary>原始正文内容参与校验，不能先反序列化再重新序列化。</summary>
    internal static void Verify(RSA rsa, string message, string signature)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("微信支付平台签名格式无效");
        }

        if (!rsa.VerifyData(System.Text.Encoding.UTF8.GetBytes(message), bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            throw new InvalidOperationException("微信支付平台验签失败");
        }
    }
}
