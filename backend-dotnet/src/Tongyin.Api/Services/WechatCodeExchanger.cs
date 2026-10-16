using System.Text.Json;
using Tongyin.Application.Auth;

namespace Tongyin.Api.Services;

/// <summary>
/// 微信 code2Session HTTP 适配器；机密凭据只留在服务端，禁止开发模拟凭据进入生产。
/// </summary>
public sealed class WechatCodeExchanger(HttpClient httpClient, IConfiguration configuration, IHostEnvironment environment) : IWechatCodeExchanger, IDisposable
{
    /// <summary>将一次性 code 换为微信 OpenID，不记录 AppSecret、code 或 session_key。</summary>
    public async Task<string> ExchangeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 512)
        {
            throw new ArgumentException("微信登录凭证无效", nameof(code));
        }

        if (code == "DEV_LOGIN")
        {
            if (environment.IsDevelopment() && configuration.GetValue<bool>("DEV_MODE"))
            {
                return "dev-openid";
            }

            throw new ArgumentException("生产环境不允许模拟登录", nameof(code));
        }

        var appId = configuration["WECHAT_APP_ID"];
        var appSecret = configuration["WECHAT_APP_SECRET"];
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
        {
            throw new InvalidOperationException("微信登录所需的服务端应用凭据尚未配置");
        }

        var uri = "https://api.weixin.qq.com/sns/jscode2session"
            + "?appid=" + Uri.EscapeDataString(appId)
            + "&secret=" + Uri.EscapeDataString(appSecret)
            + "&js_code=" + Uri.EscapeDataString(code)
            + "&grant_type=authorization_code";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("微信登录服务返回非成功状态");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var data = json.RootElement;
        if (data.TryGetProperty("errcode", out var errorCode) && errorCode.GetInt32() != 0)
        {
            if (errorCode.GetInt32() is 40029 or 40163)
            {
                throw new ArgumentException("微信登录凭证无效", nameof(code));
            }

            throw new InvalidOperationException("微信登录服务暂不可用");
        }

        if (!data.TryGetProperty("openid", out var userId) || userId.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(userId.GetString()))
        {
            throw new InvalidOperationException("微信登录服务未返回有效的用户标识");
        }

        return userId.GetString()!;
    }

    /// <summary>释放用于连接微信服务器的 HTTP 客户端。</summary>
    public void Dispose() => httpClient.Dispose();
}
