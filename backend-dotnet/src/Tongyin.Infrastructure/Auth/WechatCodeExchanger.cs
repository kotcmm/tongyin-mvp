using System.Net.Http.Json;
using Tongyin.Application.Auth;

namespace Tongyin.Infrastructure.Auth;

/// <summary>
/// 仅负责调用微信 code2Session，使用数据库中的应用凭据兑换真实 OpenID。
/// </summary>
public sealed class WechatCodeExchanger(HttpClient httpClient, IWechatAppSettingsStore settingsStore) : IWechatCodeExchanger, IDisposable
{
    private const string SessionEndpoint = "https://api.weixin.qq.com/sns/jscode2session";

    /// <summary>使用一次性 code 兑换微信身份，不处理开发模拟登录，也不返回 session_key。</summary>
    public async Task<string> ExchangeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 512 || code == "DEV_LOGIN")
        {
            throw new ArgumentException("微信登录凭证无效", nameof(code));
        }

        var settings = await settingsStore.GetAsync(cancellationToken);
        if (settings is null || string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
        {
            throw new InvalidOperationException("微信登录所需的服务端应用凭据尚未配置");
        }

        var url = $"{SessionEndpoint}?appid={Uri.EscapeDataString(settings.AppId)}"
            + $"&secret={Uri.EscapeDataString(settings.AppSecret)}"
            + $"&js_code={Uri.EscapeDataString(code)}&grant_type=authorization_code";

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("微信登录服务返回非成功状态");
        }

        var result = await response.Content.ReadFromJsonAsync<WechatCode2SessionResponse>(cancellationToken);
        if (result?.ErrorCode is 40029 or 40163)
        {
            throw new ArgumentException("微信登录凭证无效", nameof(code));
        }

        if (result?.ErrorCode is int errorCode && errorCode != 0)
        {
            throw new InvalidOperationException("微信登录服务暂不可用");
        }

        return !string.IsNullOrWhiteSpace(result?.OpenId)
            ? result.OpenId
            : throw new InvalidOperationException("微信登录服务未返回有效的用户标识");
    }

    /// <summary>释放此适配器创建时持有的 HTTP 客户端。</summary>
    public void Dispose() => httpClient.Dispose();
}
