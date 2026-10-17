using Tongyin.Application.Checkout;

namespace Tongyin.Application.Payments;

/// <summary>支付申请和已验证到账事件的事务性持久化端口。</summary>
public interface IPaymentOrderRepository
{
    /// <summary>为本人待付款订单原子登记支付已启动时间；不存在或非本人返回空。</summary>
    Task<OrderSnapshot?> ReserveAsync(string owner, string orderId, CancellationToken cancellationToken = default);

    /// <summary>以微信订单号、OpenID、金额和交易流水进行原子幂等入账；不匹配则拒绝。</summary>
    Task<PaymentConfirmationResult> ConfirmSuccessAsync(string orderId, string payerOpenId, int totalFen, string transactionId, CancellationToken cancellationToken = default);
}
