namespace Tongyin.Application.Payments;

/// <summary>已通过微信支付平台 RSA 验签和 AES 解密的成功付款通知。</summary>
public sealed class VerifiedPaymentNotification
{
    /// <summary>小程序 AppID。</summary>
    public required string AppId { get; init; }
    /// <summary>微信商户号。</summary>
    public required string MchId { get; init; }
    /// <summary>平台商户订单号。</summary>
    public required string OutTradeNo { get; init; }
    /// <summary>微信支付交易流水号，必须用于幂等校验。</summary>
    public required string TransactionId { get; init; }
    /// <summary>付款用户 OpenID。</summary>
    public required string PayerOpenId { get; init; }
    /// <summary>微信实际成功扣款金额，单位为分。</summary>
    public required int TotalFen { get; init; }
}
