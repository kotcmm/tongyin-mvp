using System.Text.Json;
using Tongyin.Application.Auth;

namespace Tongyin.Api.Services;

/// <summary>
/// 微信 code2Session HTTP 适配器；机密凭据只留在服务端，禁止开发模拟凭据进入生产。
/// </summary>
public sealed class WechatCodeExchanger(HttpClient httpClient, IConfiguration configuration, IHostEnvironment environment, IWechatAppSettingsStore settingsStore) : IWechatCodeExchanger, IDisposable
{
    /// <summary>将一次性 code 换为微信 OpenID，不记录 AppSecret、code 或 session_key。</summary>
    public async Task<string> ExchangeAsync(string code, CancellationToken cancellationToken = default)
    {
        ValidateCode(code);
        if (code == "DEV_LOGIN")
        {
            return GetDevelopmentOpenId();
        }

        var settings = await settingsStore.GetAsync(cancellationToken);
        var url = BuildSessionUrl(settings, code);
        return await RequestOpenIdAsync(url, cancellationToken);
    }

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 512)
        {
            throw new ArgumentException("微信登录凭证无效", nameof(code));
        }
    }

    private string GetDevelopmentOpenId()
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("DEV_MODE"))
        {
            throw new ArgumentException("生产环境不允许模拟登录", "code");
        }

        return "dev-openid";
    }

    private static string BuildSessionUrl(WechatAppSettings? settings, string code)
    {
        if (string.IsNullOrWhiteSpace(settings?.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
        {
            throw new InvalidOperationException("微信登录所需的服务端应用凭据尚未配置");
        }

        return "https://api.weixin.qq.com/sns/jscode2session"
            + "?appid=" + Uri.EscapeDataString(settings.AppId)
            + "&secret=" + Uri.EscapeDataString(settings.AppSecret)
            + "&js_code=" + Uri.EscapeDataString(code)
            + "&grant_type=authorization_code";
    }

    private async Task<string> RequestOpenIdAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("微信登录服务返回非成功状态");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseOpenId(document.RootElement);
    }

    private static string ParseOpenId(JsonElement result)
    {
        if (result.TryGetProperty("errcode", out var errorCode))
        {
            var code = errorCode.GetInt32();
            if (code is 40029 or 40163)
            {
                throw new ArgumentException("微信登录凭证无效", "code");
            }

            if (code != 0)
            {
                throw new InvalidOperationException("微信登录服务暂不可用");
            }
        }

        if (!result.TryGetProperty("openid", out var openId) || openId.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(openId.GetString()))
        {
            throw new InvalidOperationException("微信登录服务未返回有效的用户标识");
        }

        return openId.GetString()!;
    }

    /// <summary>释放用于连接微信服务器的 HTTP 客户端。</summary>
    public void Dispose() => httpClient.Dispose();
}
