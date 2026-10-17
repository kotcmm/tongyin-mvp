using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tongyin.Application.Checkout;
using Tongyin.Infrastructure.Payments;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Payments;

/// <summary>使用内存 HTTP 服务校验真实微信商户 RSA 签名与预下单响应验签。</summary>
public sealed class WechatPayV3GatewayTests
{
    /// <summary>预下单金额必须来自服务端订单；返回给 wx.requestPayment 的支付签名应当有效。</summary>
    [Fact]
    public async Task CreateAsync_ValidSignedResponse_ReturnsVerifiedClientParameters()
    {
        using var keys = new GatewayKeys();
        using var handler = new SignedResponseHandler(keys.SigningKey, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var gateway = new WechatPayV3Gateway(client, keys.Configuration);

        var parameters = await gateway.CreateAsync(ReadyOrder(), "owner-1", "wx-test-app");

        Assert.True(gateway.IsConfigured);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("https://api.mch.weixin.qq.com/v3/pay/transactions/jsapi", handler.RequestUrl);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        var json = request.RootElement;
        Assert.Equal("owner-1", json.GetProperty("payer").GetProperty("openid").GetString());
        Assert.Equal("order-1", json.GetProperty("out_trade_no").GetString());
        Assert.Equal(12900, json.GetProperty("amount").GetProperty("total").GetInt32());
        Assert.Equal("CNY", json.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal("https://api.example.com/api/pay/notify", json.GetProperty("notify_url").GetString());
        Assert.Equal("prepay_id=test-prepay", parameters.Package);
        Assert.Equal("RSA", parameters.SignType);

        var clientSignature = Convert.FromBase64String(parameters.PaySign);
        var message = "wx-test-app\n" + parameters.TimeStamp + "\n" + parameters.NonceStr + "\n" + parameters.Package + "\n";
        Assert.True(keys.SigningKey.VerifyData(Encoding.UTF8.GetBytes(message),
            clientSignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.True(handler.MerchantSignatureValid);
    }

    /// <summary>即使收到 HTTP 200 和 prepay_id，响应 RSA 验签失败也必须拒绝调起微信支付。</summary>
    [Fact]
    public async Task CreateAsync_InvalidWechatSignature_Rejects()
    {
        using var keys = new GatewayKeys();
        using var handler = new SignedResponseHandler(keys.SigningKey, HttpStatusCode.OK, invalidSignature: true);
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WechatPayV3Gateway(client, keys.Configuration).CreateAsync(ReadyOrder(), "owner-1", "wx-test-app"));
    }

    /// <summary>微信响应非 200 时不信任响应正文；避免将商户密钥或上游原始错误透出。</summary>
    [Fact]
    public async Task CreateAsync_ProviderFailure_Rejects()
    {
        using var keys = new GatewayKeys();
        using var handler = new SignedResponseHandler(keys.SigningKey, HttpStatusCode.BadRequest);
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WechatPayV3Gateway(client, keys.Configuration).CreateAsync(ReadyOrder(), "owner-1", "wx-test-app"));
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>没有支付预约的订单不能进入微信预下单，避免跳过并发取消保护。</summary>
    [Fact]
    public async Task CreateAsync_WithoutReservation_RejectsBeforeNetwork()
    {
        using var keys = new GatewayKeys();
        using var handler = new SignedResponseHandler(keys.SigningKey, HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        var work = new OrderSnapshot
        {
            Id = "order-1", WorkId = "work-1", PriceFen = 12900, Status = "awaiting_payment"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WechatPayV3Gateway(client, keys.Configuration).CreateAsync(work, "owner-1", "wx-test-app"));
        Assert.Equal(0, handler.Calls);
    }

    private static OrderSnapshot ReadyOrder() => new()
    {
        Id = "order-1", WorkId = "work-1", PriceFen = 12900, Status = "awaiting_payment",
        PaymentStartedAt = "2026-10-17T10:00:00+08:00"
    };

    private sealed class GatewayKeys : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "tongyin-gateway-" + Guid.NewGuid().ToString("N"));
        public RSA SigningKey { get; } = RSA.Create(2048);

        public GatewayKeys()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "merchant.pem"), SigningKey.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(Path.Combine(_directory, "platform.pem"), SigningKey.ExportSubjectPublicKeyInfoPem());
            Configuration = new WechatPayConfiguration
            {
                AppId = "wx-test-app", MchId = "mch-1", MerchantSerial = "merchant-serial",
                MerchantPrivateKeyPath = Path.Combine(_directory, "merchant.pem"),
                PlatformPublicKeyPath = Path.Combine(_directory, "platform.pem"),
                PlatformSerial = "platform-serial",
                ApiV3Key = "12345678901234567890123456789012",
                NotifyUrl = "https://api.example.com/api/pay/notify"
            };
        }
        public WechatPayConfiguration Configuration { get; }
        public void Dispose()
        {
            SigningKey.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class SignedResponseHandler(RSA rsa, HttpStatusCode statusCode, bool invalidSignature = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? RequestUrl { get; private set; }
        public string? RequestBody { get; private set; }
        public bool MerchantSignatureValid { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUrl = request.RequestUri?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var authorization = request.Headers.GetValues("Authorization").Single();
            var nonce = Extract(authorization, "nonce_str");
            var timestamp = Extract(authorization, "timestamp");
            var signature = Extract(authorization, "signature");
            var text = "POST\n/v3/pay/transactions/jsapi\n" + timestamp + "\n" + nonce + "\n" + RequestBody + "\n";
            MerchantSignatureValid = rsa.VerifyData(Encoding.UTF8.GetBytes(text),
                Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var responseBody = """{"prepay_id":"test-prepay"}""";
            var response = new HttpResponseMessage(statusCode) { Content = new StringContent(responseBody) };
            var responseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var responseNonce = "response-nonce";
            var signed = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(
                responseTimestamp + "\n" + responseNonce + "\n" + responseBody + "\n"),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            response.Headers.TryAddWithoutValidation("Wechatpay-Timestamp", responseTimestamp);
            response.Headers.TryAddWithoutValidation("Wechatpay-Nonce", responseNonce);
            response.Headers.TryAddWithoutValidation("Wechatpay-Serial", "platform-serial");
            response.Headers.TryAddWithoutValidation("Wechatpay-Signature", invalidSignature ? "invalid-signature" : signed);
            return response;
        }

        private static string Extract(string source, string name)
        {
            var match = Regex.Match(source, name + "=\"([^\"]*)\"", RegexOptions.CultureInvariant);
            if (!match.Success) throw new InvalidOperationException("商户签名格式无效");
            return match.Groups[1].Value;
        }
    }
}
