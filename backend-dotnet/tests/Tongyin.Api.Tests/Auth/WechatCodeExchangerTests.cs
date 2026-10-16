using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tongyin.Api.Services;
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

    /// <summary>开发模拟登录只在 Development 且 DEV_MODE 显式启用时生效。</summary>
    [Fact]
    public async Task ExchangeAsync_DevLogin_WhenDevelopmentAndEnabled_ReturnsFixedTestOpenId()
    {
        using var handler = new WxHttpHandler("{}");
        using var client = new HttpClient(handler);
        using var exchanger = NewExchanger(client, development: true, devMode: true);

        Assert.Equal("dev-openid", await exchanger.ExchangeAsync("DEV_LOGIN"));
        Assert.Null(handler.RequestUri);
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

    private static WechatCodeExchanger NewExchanger(HttpClient httpClient, bool development = false, bool devMode = false)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WECHAT_APP_ID"] = "test-app-id",
            ["WECHAT_APP_SECRET"] = "test-app-secret",
            ["DEV_MODE"] = devMode ? "true" : "false"
        }).Build();
        return new WechatCodeExchanger(httpClient, config, new TestEnvironment
        {
            EnvironmentName = development ? "Development" : "Production"
        });
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tongyin.Api.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
