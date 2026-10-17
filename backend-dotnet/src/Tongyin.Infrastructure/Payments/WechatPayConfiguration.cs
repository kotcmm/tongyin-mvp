namespace Tongyin.Infrastructure.Payments;

/// <summary>微信支付 V3 商户凭据配置，仅由服务器环境变量填充，不返回到小程序。</summary>
public sealed class WechatPayConfiguration
{
    /// <summary>旧 Go 配置的备用小程序 AppID，优先使用数据库中已保存的 AppID。</summary>
    public string AppId { get; init; } = "";
    /// <summary>商户号。</summary>
    public string MchId { get; init; } = "";
    /// <summary>商户证书序列号。</summary>
    public string MerchantSerial { get; init; } = "";
    /// <summary>商户 RSA 私钥 PEM 文件绝对路径。</summary>
    public string MerchantPrivateKeyPath { get; init; } = "";
    /// <summary>微信支付平台证书或者平台公钥 PEM 文件路径。</summary>
    public string PlatformPublicKeyPath { get; init; } = "";
    /// <summary>用于校验回调和微信应答的微信支付公钥 ID 或平台证书序列号。</summary>
    public string PlatformSerial { get; init; } = "";
    /// <summary>微信支付 API V3 32 字节对称密钥。</summary>
    public string ApiV3Key { get; init; } = "";
    /// <summary>微信支付接收异步通知的公网 HTTPS URL。</summary>
    public string NotifyUrl { get; init; } = "";

    /// <summary>启动支付需要完整的商户凭据、公钥和固定的公网回调 URL。</summary>
    public bool IsReady =>
        !string.IsNullOrWhiteSpace(MchId)
        && !string.IsNullOrWhiteSpace(MerchantSerial)
        && File.Exists(MerchantPrivateKeyPath)
        && File.Exists(PlatformPublicKeyPath)
        && !string.IsNullOrWhiteSpace(PlatformSerial)
        && System.Text.Encoding.UTF8.GetByteCount(ApiV3Key) == 32
        && Uri.TryCreate(NotifyUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.HostNameType == UriHostNameType.Dns
        && !uri.IsLoopback && uri.Port == 443 && uri.UserInfo.Length == 0
        && uri.Fragment.Length == 0 && uri.Query.Length == 0;
}
