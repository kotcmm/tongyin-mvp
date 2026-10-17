namespace Tongyin.Application.Checkout;

/// <summary>订单金额及免费图片生成次数的业务配置值，供应用服务与 HTTP 接口共用。</summary>
public sealed class MiniappBusinessSettings
{
    /// <summary>商品售价，单位为分。</summary>
    public int PriceFen { get; }

    /// <summary>每件作品的免费生成次数。</summary>
    public int FreeImageAttempts { get; }

    /// <summary>仅处理业务值及默认规则，不依赖 ASP.NET Core 配置系统。</summary>
    public MiniappBusinessSettings(int priceFen, int freeImageAttempts)
    {
        PriceFen = priceFen > 0 ? priceFen : 12900;
        FreeImageAttempts = freeImageAttempts > 0 ? freeImageAttempts : 2;
    }
}
