using Tongyin.Application.Checkout;

namespace Tongyin.Application.Payments;

/// <summary>微信支付 V3 JSAPI 预下单和小程序调起支付参数的外部支付端口。</summary>
public interface IWechatPayGateway
{
    /// <summary>商户号、签名密钥、平台验签公钥、回调地址和 APIv3 密钥是否已配置。</summary>
    bool IsConfigured { get; }

    /// <summary>使用服务端原始订单金额和当前 OpenID 创建预付订单，返回小程序签名参数。</summary>
    Task<MiniappPaymentParameters> CreateAsync(OrderSnapshot order, string payerOpenId, string appId, CancellationToken cancellationToken = default);
}
