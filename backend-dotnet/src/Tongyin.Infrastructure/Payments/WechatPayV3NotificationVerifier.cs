using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tongyin.Application.Payments;

namespace Tongyin.Infrastructure.Payments;

/// <summary>先验证微信支付平台 RSA 签名和有效时间，再使用 APIv3 Key 解密支付通知。</summary>
public sealed class WechatPayV3NotificationVerifier(WechatPayConfiguration configuration) : IWechatPayNotificationVerifier
{
    /// <summary>只接受匹配平台密钥序列号的 5 分钟内已验签成功交易；非成功事件不更新订单。</summary>
    public VerifiedPaymentNotification? Verify(string body, string timestamp, string nonce, string signature, string serial)
    {
        if (!configuration.IsReady)
        {
            throw new InvalidOperationException("微信支付回调尚未配置");
        }
        if (serial != configuration.PlatformSerial || string.IsNullOrWhiteSpace(nonce)
            || string.IsNullOrWhiteSpace(signature)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 300
            || seconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300)
        {
            throw new InvalidOperationException("微信支付回调序列号或时间戳无效");
        }

        using (var publicKey = WechatPayKeyLoader.LoadPlatformKey(configuration.PlatformPublicKeyPath))
        {
            WechatPayKeyLoader.Verify(publicKey, timestamp + "\n" + nonce + "\n" + body + "\n", signature);
        }

        PaymentEvent? callback;
        try
        {
            callback = JsonSerializer.Deserialize<PaymentEvent>(body);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("微信支付回调正文格式无效");
        }

        if (callback?.EventType != "TRANSACTION.SUCCESS")
        {
            return null;
        }

        var resource = callback.Resource ?? throw new InvalidOperationException("微信支付交易资源缺失");
        if (resource.Algorithm != "AEAD_AES_256_GCM"
            || string.IsNullOrEmpty(resource.Nonce) || Encoding.UTF8.GetByteCount(resource.Nonce) != 12
            || string.IsNullOrEmpty(resource.Ciphertext))
        {
            throw new InvalidOperationException("微信支付交易资源加密格式无效");
        }

        byte[] encrypted;
        try
        {
            encrypted = Convert.FromBase64String(resource.Ciphertext);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("微信支付交易密文格式无效");
        }
        if (encrypted.Length <= 16)
        {
            throw new InvalidOperationException("微信支付交易密文长度无效");
        }

        var plaintext = new byte[encrypted.Length - 16];
        try
        {
            var key = Encoding.UTF8.GetBytes(configuration.ApiV3Key);
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Encoding.UTF8.GetBytes(resource.Nonce),
                encrypted.AsSpan(0, plaintext.Length), encrypted.AsSpan(plaintext.Length, 16),
                plaintext, Encoding.UTF8.GetBytes(resource.AssociatedData ?? ""));
            var transaction = JsonSerializer.Deserialize<PaymentTransaction>(plaintext)
                ?? throw new InvalidOperationException("微信支付解密交易内容为空");
            if (transaction.TradeState != "SUCCESS" || transaction.MchId != configuration.MchId
                || string.IsNullOrWhiteSpace(transaction.AppId)
                || string.IsNullOrWhiteSpace(transaction.OutTradeNo)
                || string.IsNullOrWhiteSpace(transaction.TransactionId)
                || string.IsNullOrWhiteSpace(transaction.Payer?.OpenId)
                || transaction.Amount is not { Total: > 0 })
            {
                throw new InvalidOperationException("微信支付交易状态、商户号或金额无效");
            }

            return new VerifiedPaymentNotification
            {
                AppId = transaction.AppId,
                MchId = transaction.MchId,
                OutTradeNo = transaction.OutTradeNo,
                TransactionId = transaction.TransactionId,
                PayerOpenId = transaction.Payer.OpenId,
                TotalFen = transaction.Amount.Total
            };
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("微信支付通知解密验证失败");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("微信支付解密交易内容无效");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    private sealed class PaymentEvent
    {
        [JsonPropertyName("event_type")]
        public string EventType { get; init; } = "";
        [JsonPropertyName("resource")]
        public EncryptedResource? Resource { get; init; }
    }

    private sealed class EncryptedResource
    {
        [JsonPropertyName("algorithm")]
        public string Algorithm { get; init; } = "";
        [JsonPropertyName("nonce")]
        public string Nonce { get; init; } = "";
        [JsonPropertyName("ciphertext")]
        public string Ciphertext { get; init; } = "";
        [JsonPropertyName("associated_data")]
        public string AssociatedData { get; init; } = "";
    }

    private sealed class PaymentTransaction
    {
        [JsonPropertyName("appid")]
        public string AppId { get; init; } = "";
        [JsonPropertyName("mchid")]
        public string MchId { get; init; } = "";
        [JsonPropertyName("out_trade_no")]
        public string OutTradeNo { get; init; } = "";
        [JsonPropertyName("transaction_id")]
        public string TransactionId { get; init; } = "";
        [JsonPropertyName("trade_state")]
        public string TradeState { get; init; } = "";
        [JsonPropertyName("amount")]
        public PaymentAmount? Amount { get; init; }
        [JsonPropertyName("payer")]
        public PaymentPayer? Payer { get; init; }
    }

    private sealed class PaymentAmount
    {
        [JsonPropertyName("total")]
        public int Total { get; init; }
    }

    private sealed class PaymentPayer
    {
        [JsonPropertyName("openid")]
        public string OpenId { get; init; } = "";
    }
}
