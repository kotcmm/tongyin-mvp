using Tongyin.Application.Auth;

namespace Tongyin.Infrastructure.Auth;

/// <summary>
/// 只在受控开发环境注册的模拟微信身份适配器，其他 code 委托真实微信交换服务。
/// </summary>
public sealed class DevelopmentWechatCodeExchanger(IWechatCodeExchanger actual) : IWechatCodeExchanger, IDisposable
{
    /// <summary>开发测试 code 返回固定身份，正常 code 仍要求微信服务器验证。</summary>
    public Task<string> ExchangeAsync(string code, CancellationToken cancellationToken = default)
        => code == "DEV_LOGIN" ? Task.FromResult("dev-openid") : actual.ExchangeAsync(code, cancellationToken);

    /// <summary>释放持有的真实微信交换适配器。</summary>
    public void Dispose()
    {
        if (actual is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
