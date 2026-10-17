using System.Net;
using System.Text;
using Tongyin.Infrastructure.Auth;
using Tongyin.Application.Auth;
using Xunit;

namespace Tongyin.Api.Tests.Auth;

/// <summary>
/// 验证 code2Session 官方接口请求及响应处理，全程由模拟 HTTP 处理器截获，不访问真实微信服务。
/// </summary>
public sealed class WechatCodeExchangerTests
{
    /// <summary>服务器以固定 HTTPS 地址交换 code，只取 OpenID，不返回 session_key。</summary>
    [Fact]
    public async Task ExchangeAsync_ValidResponse_ReturnsOnlyOpenId()
    {
        using var handler = new WxHttpHandler("""{"openid":"wechat-openid","session_key":"private-session-key"}""");
        using var client = new HttpClient(handler);
        using var exchanger = NewExchanger(client);

        var openId = await exchanger.ExchangeAsync("one-time-code");

        Assert.Equal("wechat-openid", openId);
        Assert.Equal("api.weixin.qq.com", handler.RequestUri?.Host);
        Assert.Equal("/sns/jscode2session", handler.RequestUri?.AbsolutePath);
        Assert.Contains("appid=test-app-id", handler.RequestUri?.Query);
        Assert.Contains("secret=test-app-secret", handler.RequestUri?.Query);
        Assert.Contains("js_code=one-time-code", handler.RequestUri?.Query);
        Assert.Contains("grant_type=authorization_code", handler.RequestUri?.Query);
    }

    /// <summary>微信明确报告一次性登录凭证失效时，不接受伪造身份。</summary>
    [Fact]
    public async Task ExchangeAsync_InvalidWechatCode_Rejects()
    {
        using var handler = new WxHttpHandler("""{"errcode":40029,"errmsg":"invalid code"}""");
        using var client = new HttpClient(handler);
        using var exchanger = NewExchanger(client);

        await Assert.ThrowsAsync<ArgumentException>(() => exchanger.ExchangeAsync("bad-code"));
    }

    /// <summary>微信响应缺少用户标识时不得签发任何登录态。</summary>
    [Fact]
    public async Task ExchangeAsync_WithoutOpenId_Rejects()
    {
        using var handler = new WxHttpHandler("""{"session_key":"only-secret"}""");
        using var client = new HttpClient(handler);
        using var exchanger = NewExchanger(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => exchanger.ExchangeAsync("one-time-code"));
    }

    /// <summary>开发模拟登录只由开发专用适配器处理，不读取数据库或发起微信 HTTP 请求。</summary>
    [Fact]
    public async Task DevelopmentExchanger_DevCode_ReturnsFixedOpenIdWithoutWechatRequest()
    {
        using var handler = new WxHttpHandler("{}");
        using var client = new HttpClient(handler);
        var settings = new FakeSettingsStore();
        using var exchanger = new DevelopmentWechatCodeExchanger(new WechatCodeExchanger(client, settings));

        Assert.Equal("dev-openid", await exchanger.ExchangeAsync("DEV_LOGIN"));
        Assert.Equal(0, settings.ReadCount);
        Assert.Null(handler.RequestUri);
    }

    /// <summary>生产适配器永远拒绝内部测试 code，不能访问数据库或请求微信接口。</summary>
    [Fact]
    public async Task ProductionExchanger_DevCode_IsRejectedBeforeExternalAccess()
    {
        using var handler = new WxHttpHandler("{}");
        using var client = new HttpClient(handler);
        var settings = new FakeSettingsStore();
        using var exchanger = new WechatCodeExchanger(client, settings);

        await Assert.ThrowsAsync<ArgumentException>(() => exchanger.ExchangeAsync("DEV_LOGIN"));

        Assert.Equal(0, settings.ReadCount);
        Assert.Null(handler.RequestUri);
    }

    /// <summary>开发专用适配器对于真实微信 code 应委托给生产实现，不绕过身份校验。</summary>
    [Fact]
    public async Task DevelopmentExchanger_RealCode_UsesWeChatExchange()
    {
        using var handler = new WxHttpHandler("""{"openid":"normal-openid"}""");
        using var client = new HttpClient(handler);
        var settings = new FakeSettingsStore();
        using var exchanger = new DevelopmentWechatCodeExchanger(new WechatCodeExchanger(client, settings));

        Assert.Equal("normal-openid", await exchanger.ExchangeAsync("real-code"));
        Assert.Equal(1, settings.ReadCount);
        Assert.Equal("api.weixin.qq.com", handler.RequestUri?.Host);
    }

    /// <summary>微信返回不合法的 JSON 时应按上游故障处理，不能当成成功的 OpenID。</summary>
    [Fact]
    public async Task ExchangeAsync_InvalidJson_Rejects()
    {
        using var handler = new WxHttpHandler("not-json");
        using var client = new HttpClient(handler);
        using var exchanger = NewExchanger(client);

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => exchanger.ExchangeAsync("one-time-code"));
    }

    private static WechatCodeExchanger NewExchanger(HttpClient httpClient)
        => new(httpClient, new FakeSettingsStore());

    /// <summary>数据库凭据发生变化时，后续微信登录请求使用更新后的密钥。</summary>
    [Fact]
    public async Task ExchangeAsync_SettingsChanged_UsesUpdatedCredentials()
    {
        using var handler = new WxHttpHandler("""{"openid":"wechat-openid"}""");
        using var client = new HttpClient(handler);
        var settings = new FakeSettingsStore();
        using var exchanger = new WechatCodeExchanger(client, settings);
        await exchanger.ExchangeAsync("first-code");
        Assert.Contains("appid=test-app-id", handler.RequestUri?.Query);

        settings.Set("wx-changed", "changed-secret");
        await exchanger.ExchangeAsync("second-code");
        Assert.Contains("appid=wx-changed", handler.RequestUri?.Query);
        Assert.Contains("secret=changed-secret", handler.RequestUri?.Query);
        Assert.DoesNotContain("test-app-secret", handler.RequestUri?.Query);
    }

    /// <summary>数据库未配置时，适配器不得发送微信请求。</summary>
    [Fact]
    public async Task ExchangeAsync_NoDatabaseCredentials_FailsBeforeNetwork()
    {
        using var handler = new WxHttpHandler("{}");
        using var client = new HttpClient(handler);
        var settings = new FakeSettingsStore();
        settings.Clear();
        using var exchanger = new WechatCodeExchanger(client, settings);

        await Assert.ThrowsAsync<InvalidOperationException>(() => exchanger.ExchangeAsync("real-code"));

        Assert.Equal(1, settings.ReadCount);
        Assert.Null(handler.RequestUri);
    }

    /// <summary>微信非 code 错误不能作为成功响应。</summary>
    [Fact]
    public async Task ExchangeAsync_WechatServiceError_IsRejected()
    {
        using var handler = new WxHttpHandler("""{"errcode":45011,"openid":"forged-user"}""");
        using var client = new HttpClient(handler);
        using var exchanger = NewExchanger(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => exchanger.ExchangeAsync("real-code"));
    }

    private sealed class FakeSettingsStore : IWechatAppSettingsStore
    {
        private WechatAppSettings? _current = new() { AppId = "test-app-id", AppSecret = "test-app-secret" };

        public int ReadCount { get; private set; }

        public Task<WechatAppSettings?> GetAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(_current);
        }

        public Task SaveAsync(string appId, string? appSecret, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Set(string appId, string appSecret)
            => _current = new WechatAppSettings { AppId = appId, AppSecret = appSecret };

        public void Clear() => _current = null;
    }

    private sealed class WxHttpHandler(string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
