using Tongyin.Application.Auth;

namespace Tongyin.Application.Payments;

/// <summary>优先使用数据库中配置的微信小程序 AppID，兼容旧 Go 环境变量备用值。</summary>
public sealed class WechatPaymentAppIdResolver(IWechatAppSettingsStore appSettingsStore, string fallbackAppId)
{
    /// <summary>返回用于支付的实际小程序 AppID，避免与当前登录应用不一致。</summary>
    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await appSettingsStore.GetAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(settings?.AppId) ? fallbackAppId : settings.AppId;
    }
}
