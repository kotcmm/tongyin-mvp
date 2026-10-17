namespace Tongyin.Application.Payments;

/// <summary>微信支付平台原始签名、密钥序列号或加密交易资源校验失败的明确异常。</summary>
public sealed class PaymentNotificationValidationException : Exception
{
    /// <summary>内部保留原始校验错误用于诊断；控制器绝不将其内容返回微信请求方。</summary>
    public PaymentNotificationValidationException(string message, Exception innerException)
        : base(message, innerException) { }
}
