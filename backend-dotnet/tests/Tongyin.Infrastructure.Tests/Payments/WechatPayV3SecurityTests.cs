using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tongyin.Infrastructure.Payments;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Payments;

/// <summary>真实 RSA-SHA256/AES-256-GCM 测试向量验证微信支付通知，任何校验缺失不得修改订单。</summary>
public sealed class WechatPayV3SecurityTests
{
    /// <summary>验签通过并解密交易详情时，返回强类型交易字段供应用层做业务核对。</summary>
    [Fact]
    public void Verify_SignedEncryptedPayment_ReturnsVerifiedTransaction()
    {
        using var keys = new TestKeys();
        var verifier = new WechatPayV3NotificationVerifier(keys.Configuration);
        var (body, timestamp, nonce, signature) = keys.ValidPayment();

        var result = verifier.Verify(body, timestamp, nonce, signature, "platform-serial");

        Assert.NotNull(result);
        Assert.Equal("order-1", result.OutTradeNo);
        Assert.Equal("tx-001", result.TransactionId);
        Assert.Equal("owner-1", result.PayerOpenId);
        Assert.Equal(12900, result.TotalFen);
        Assert.Equal("wx-test-app", result.AppId);
    }

    /// <summary>篡改原始 HTTP Body 或签名时拒绝，不能继续解密或结算。</summary>
    [Fact]
    public void Verify_TamperedBodyOrSignature_Rejects()
    {
        using var keys = new TestKeys();
        var verifier = new WechatPayV3NotificationVerifier(keys.Configuration);
        var (body, timestamp, nonce, signature) = keys.ValidPayment();

        Assert.Throws<InvalidOperationException>(() => verifier.Verify(body + " ", timestamp, nonce, signature, "platform-serial"));
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(body, timestamp, nonce, "not-valid-signature", "platform-serial"));
    }

    /// <summary>时间戳过期、平台序列号不匹配均禁止回调被接受。</summary>
    [Fact]
    public void Verify_ExpiredOrWrongPlatformKey_Rejects()
    {
        using var keys = new TestKeys();
        var verifier = new WechatPayV3NotificationVerifier(keys.Configuration);
        var (body, timestamp, nonce, signature) = keys.ValidPayment();

        Assert.Throws<InvalidOperationException>(() => verifier.Verify(body, "100", nonce, signature, "platform-serial"));
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(body, timestamp, nonce, signature, "other-serial"));
    }

    /// <summary>极端 Unix 时间戳必须被安全拒绝，不能因整数溢出抛出非预期异常。</summary>
    [Fact]
    public void Verify_MinimumTimestamp_ReturnsValidationError()
    {
        using var keys = new TestKeys();
        var verifier = new WechatPayV3NotificationVerifier(keys.Configuration);
        var (body, _, nonce, signature) = keys.ValidPayment();

        Assert.Throws<InvalidOperationException>(() =>
            verifier.Verify(body, long.MinValue.ToString(), nonce, signature, "platform-serial"));
    }

    /// <summary>没有对应密钥的伪造密文，即使回调正文通过 RSA 验签，也不能被解密。</summary>
    [Fact]
    public void Verify_AuthenticatedButInvalidCiphertext_Rejects()
    {
        using var keys = new TestKeys();
        var verifier = new WechatPayV3NotificationVerifier(keys.Configuration);
        var (body, _, nonce, _) = keys.ValidPayment();
        var malformed = body.Replace("AEAD_AES_256_GCM", "AEAD_INVALID");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = keys.Sign(timestamp + "\n" + nonce + "\n" + malformed + "\n");

        Assert.Throws<InvalidOperationException>(() => verifier.Verify(malformed, timestamp, nonce, signature, "platform-serial"));
    }

    /// <summary>非成功通知即使合法验签，也不得当作成功交易。</summary>
    [Fact]
    public void Verify_OtherEvent_ReturnsNoTransaction()
    {
        using var keys = new TestKeys();
        var verifier = new WechatPayV3NotificationVerifier(keys.Configuration);
        var body = """{"event_type":"TRANSACTION.CLOSED"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = "random-notify-nonce";

        Assert.Null(verifier.Verify(body, timestamp, nonce,
            keys.Sign(timestamp + "\n" + nonce + "\n" + body + "\n"), "platform-serial"));
    }

    private sealed class TestKeys : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "tongyin-pay-security-" + Guid.NewGuid().ToString("N"));
        private readonly RSA _rsa = RSA.Create(2048);
        private readonly byte[] _apiKey = Encoding.UTF8.GetBytes("12345678901234567890123456789012");

        public TestKeys()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "platform.pem"), _rsa.ExportSubjectPublicKeyInfoPem());
            File.WriteAllText(Path.Combine(_folder, "merchant.pem"), _rsa.ExportPkcs8PrivateKeyPem());
            Configuration = new WechatPayConfiguration
            {
                MchId = "mch-1", MerchantSerial = "merchant-serial", AppId = "wx-test-app",
                MerchantPrivateKeyPath = Path.Combine(_folder, "merchant.pem"),
                PlatformPublicKeyPath = Path.Combine(_folder, "platform.pem"),
                PlatformSerial = "platform-serial",
                ApiV3Key = Encoding.UTF8.GetString(_apiKey),
                NotifyUrl = "https://api.example.com/api/pay/notify"
            };
        }
        public WechatPayConfiguration Configuration { get; }

        public string Sign(string message)
            => Convert.ToBase64String(_rsa.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        public (string Body, string Timestamp, string Nonce, string Signature) ValidPayment()
        {
            var plain = JsonSerializer.Serialize(new
            {
                out_trade_no = "order-1", transaction_id = "tx-001", trade_state = "SUCCESS",
                appid = "wx-test-app", mchid = "mch-1",
                amount = new { total = 12900 },
                payer = new { openid = "owner-1" }
            });
            var iv = RandomNumberGenerator.GetBytes(12);
            var clear = Encoding.UTF8.GetBytes(plain);
            var cipher = new byte[clear.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(_apiKey, 16))
                aes.Encrypt(iv, clear, cipher, tag, Encoding.UTF8.GetBytes("associated"));
            var body = JsonSerializer.Serialize(new
            {
                event_type = "TRANSACTION.SUCCESS",
                resource = new
                {
                    algorithm = "AEAD_AES_256_GCM",
                    associated_data = "associated",
                    nonce = Encoding.UTF8.GetString(iv.Select(v => (byte)('a' + v % 26)).ToArray()),
                    ciphertext = ""
                }
            });
            // 使用可打印、仍具有 12 字节长度的 AES GCM 随机数。
            var printableIv = Encoding.ASCII.GetBytes("123456789012");
            using (var aes = new AesGcm(_apiKey, 16))
                aes.Encrypt(printableIv, clear, cipher, tag, Encoding.UTF8.GetBytes("associated"));
            body = JsonSerializer.Serialize(new
            {
                event_type = "TRANSACTION.SUCCESS",
                resource = new
                {
                    algorithm = "AEAD_AES_256_GCM", associated_data = "associated",
                    nonce = "123456789012", ciphertext = Convert.ToBase64String([.. cipher, .. tag])
                }
            });
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var nonce = "notification-nonce";
            return (body, timestamp, nonce, Sign(timestamp + "\n" + nonce + "\n" + body + "\n"));
        }
        public void Dispose()
        {
            _rsa.Dispose();
            Directory.Delete(_folder, true);
        }
    }
}
