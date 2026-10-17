namespace Tongyin.Application.Payments;

/// <summary>微信支付原始通知的验签、平台序列号校验及 AES-GCM 解密端口。</summary>
public interface IWechatPayNotificationVerifier
{
    /// <summary>只对通过原始报文验签并成功解密的 TRANSACTION.SUCCESS 返回交易；其他类型返回空。</summary>
    VerifiedPaymentNotification? Verify(string body, string timestamp, string nonce, string signature, string serial);
}
