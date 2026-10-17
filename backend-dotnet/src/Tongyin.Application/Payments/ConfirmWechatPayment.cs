namespace Tongyin.Application.Payments;

/// <summary>微信回调只有先验签、解密、核对商户和应用后才能原子更新订单状态。</summary>
public sealed class ConfirmWechatPayment(IPaymentOrderRepository repository, IWechatPayNotificationVerifier verifier)
{
    /// <summary>收到原始通知报文后核实交易，并将实际入账写成 paid 或标记待退款。</summary>
    public async Task<PaymentConfirmationResult?> ExecuteAsync(string rawBody, string timestamp, string nonce, string signature, string serial, string expectedAppId, CancellationToken cancellationToken = default)
    {
        var notification = verifier.Verify(rawBody, timestamp, nonce, signature, serial);
        if (notification is null)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(expectedAppId)
            || notification.AppId != expectedAppId)
        {
            throw new InvalidOperationException("支付通知的 AppID 与当前微信小程序不一致");
        }
        return await repository.ConfirmSuccessAsync(notification.OutTradeNo, notification.PayerOpenId, notification.TotalFen, notification.TransactionId, cancellationToken);
    }
}
