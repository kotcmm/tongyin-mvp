namespace Tongyin.Application.Payments;

/// <summary>先持久化支付尝试再调用微信商户接口，避免不确定扣款期间撤销订单。</summary>
public sealed class StartWechatPayment(IPaymentOrderRepository repository, IWechatPayGateway gateway)
{
    /// <summary>商户支付能力配置是否完整。</summary>
    public bool IsConfigured => gateway.IsConfigured;

    /// <summary>仅本人待付款订单可获取 JSAPI 预支付参数；网络不确定状态不撤销支付标记。</summary>
    public async Task<MiniappPaymentParameters?> ExecuteAsync(string owner, string orderId, string appId, CancellationToken cancellationToken = default)
    {
        if (!gateway.IsConfigured || string.IsNullOrWhiteSpace(appId))
        {
            throw new InvalidOperationException("微信商户配置尚未完成，暂时不能付款");
        }

        var order = await repository.ReserveAsync(owner, orderId, cancellationToken);
        return order is null ? null : await gateway.CreateAsync(order, owner, appId, cancellationToken);
    }
}
