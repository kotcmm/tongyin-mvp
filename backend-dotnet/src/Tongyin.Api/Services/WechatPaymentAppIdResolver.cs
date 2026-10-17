using Tongyin.Application.Auth;
using Tongyin.Infrastructure.Payments;

namespace Tongyin.Api.Services;

/// <summary>从当前数据库微信小程序凭据中解析 AppID，兼容旧 Go 环境变量的备用配置。</summary>
public sealed class WechatPaymentAppIdResolver(IWechatAppSettingsStore appSettingsStore, WechatPayConfiguration configuration)
{
    /// <summary>优先读取管理员设置的实际小程序 AppID，避免支付和微信登录使用不同应用。</summary>
    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await appSettingsStore.GetAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(settings?.AppId) ? configuration.AppId : settings.AppId;
    }
}
