namespace Tongyin.Application.Auth;

/// <summary>管理微信应用凭据，仅公开非敏感状态并校验管理员输入。</summary>
public sealed class ManageWechatAppSettings(IWechatAppSettingsStore store)
{
    /// <summary>返回 AppID 和是否已配置 AppSecret，不返回密钥。</summary>
    public async Task<WechatAppSettingsStatus> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await store.GetAsync(cancellationToken);
        return new WechatAppSettingsStatus
        {
            AppId = settings?.AppId ?? string.Empty,
            HasAppSecret = !string.IsNullOrEmpty(settings?.AppSecret)
        };
    }

    /// <summary>校验并保存管理员输入；留空 AppSecret 表示保持原密钥。</summary>
    public async Task<WechatAppSettingsStatus> SaveAsync(string? appId, string? appSecret, CancellationToken cancellationToken = default)
    {
        var normalizedAppId = appId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedAppId) || normalizedAppId.Length > 128
            || normalizedAppId.Any(char.IsControl))
        {
            throw new ArgumentException("AppID 不能为空，且不能超过 128 个字符", nameof(appId));
        }

        if (appSecret is { Length: > 1024 } || (appSecret is not null && appSecret.Any(char.IsControl)))
        {
            throw new ArgumentException("AppSecret 格式无效", nameof(appSecret));
        }

        await store.SaveAsync(normalizedAppId, string.IsNullOrEmpty(appSecret) ? null : appSecret, cancellationToken);
        return await ReadStatusAsync(cancellationToken);
    }
}
