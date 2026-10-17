using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tongyin.Application.Checkout;
using Tongyin.Application.Payments;

namespace Tongyin.Infrastructure.Payments;

/// <summary>按微信支付 APIv3 的商户 RSA-SHA256 签名协议创建 JSAPI/小程序预支付订单。</summary>
public sealed class WechatPayV3Gateway(HttpClient client, WechatPayConfiguration configuration) : IWechatPayGateway
{
    private const string Route = "/v3/pay/transactions/jsapi";
    private static readonly Uri Endpoint = new("https://api.mch.weixin.qq.com" + Route);

    /// <summary>只有商户签名私钥、平台验签公钥、APIv3 Key 和回调地址全部设置后才可支付。</summary>
    public bool IsConfigured => configuration.IsReady;

    /// <summary>服务端金额和付款人 OpenID 进入商户签名请求；只接受验签成功的微信支付预下单应答。</summary>
    public async Task<MiniappPaymentParameters> CreateAsync(OrderSnapshot order, string payerOpenId, string appId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(appId)
            || string.IsNullOrWhiteSpace(payerOpenId)
            || order.Status != "awaiting_payment" || order.PriceFen <= 0
            || string.IsNullOrWhiteSpace(order.PaymentStartedAt))
        {
            throw new InvalidOperationException("商户参数或订单支付状态无效");
        }

        var body = JsonSerializer.Serialize(new
        {
            appid = appId,
            mchid = configuration.MchId,
            description = "童印 · 儿童画定制白模",
            out_trade_no = order.Id,
            notify_url = configuration.NotifyUrl,
            amount = new { total = order.PriceFen, currency = "CNY" },
            payer = new { openid = payerOpenId }
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        using var merchantKey = WechatPayKeyLoader.LoadMerchantKey(configuration.MerchantPrivateKeyPath);
        var signature = WechatPayKeyLoader.Sign(merchantKey,
            "POST\n" + Route + "\n" + timestamp + "\n" + nonce + "\n" + body + "\n");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var authorization = "WECHATPAY2-SHA256-RSA2048 mchid=\"" + configuration.MchId
            + "\",nonce_str=\"" + nonce + "\",timestamp=\"" + timestamp
            + "\",serial_no=\"" + configuration.MerchantSerial + "\",signature=\"" + signature + "\"";
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("微信支付连接失败，提交状态未知，请核对订单");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("微信支付请求超时，提交状态未知，请核对订单");
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException("微信支付预下单失败，请核实商户配置或订单状态");
            }

            if (response.Content.Headers.ContentLength > 8192)
            {
                throw new InvalidOperationException("微信支付预下单应答长度无效");
            }
            await response.Content.LoadIntoBufferAsync(8192, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            var responseTimestamp = GetHeader(response, "Wechatpay-Timestamp");
            var responseNonce = GetHeader(response, "Wechatpay-Nonce");
            var responseSerial = GetHeader(response, "Wechatpay-Serial");
            var responseSignature = GetHeader(response, "Wechatpay-Signature");
            if (responseSerial != configuration.PlatformSerial || string.IsNullOrWhiteSpace(responseNonce)
                || !long.TryParse(responseTimestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var responseUnix)
                || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - responseUnix) > 300)
            {
                throw new InvalidOperationException("微信预下单应答的平台签名信息无效");
            }

            using (var publicKey = WechatPayKeyLoader.LoadPlatformKey(configuration.PlatformPublicKeyPath))
            {
                WechatPayKeyLoader.Verify(publicKey, responseTimestamp + "\n" + responseNonce + "\n" + responseBody + "\n", responseSignature);
            }

            PrepayResponse? prepay;
            try
            {
                prepay = JsonSerializer.Deserialize<PrepayResponse>(responseBody);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("微信预支付数据格式错误");
            }

            if (string.IsNullOrWhiteSpace(prepay?.PrepayId))
            {
                throw new InvalidOperationException("微信支付没有返回有效预支付编号");
            }

            var clientTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var clientNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var package = "prepay_id=" + prepay.PrepayId;
            return new MiniappPaymentParameters
            {
                TimeStamp = clientTimestamp,
                NonceStr = clientNonce,
                Package = package,
                PaySign = WechatPayKeyLoader.Sign(merchantKey, appId + "\n" + clientTimestamp + "\n" + clientNonce + "\n" + package + "\n")
            };
        }
    }

    private static string GetHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var items))
        {
            return "";
        }
        var values = items.ToArray();
        return values.Length == 1 ? values[0] : "";
    }

    private sealed class PrepayResponse
    {
        [JsonPropertyName("prepay_id")]
        public string PrepayId { get; init; } = "";
    }
}
