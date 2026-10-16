namespace Tongyin.Application.Auth;

/// <summary>从数据库读取及修改微信小程序应用凭据的存储端口。</summary>
public interface IWechatAppSettingsStore
{
    /// <summary>获取当前凭据；未设置时返回空。</summary>
    Task<WechatAppSettings?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>持久化 AppID 和新密钥；AppSecret 为空时保留已有密钥。</summary>
    Task SaveAsync(string appId, string? appSecret, CancellationToken cancellationToken = default);
}
