namespace Tongyin.Api.Services;

/// <summary>统一小程序展示和服务端图片生成准入所使用的售价、免费次数配置。</summary>
public sealed class MiniappBusinessSettings
{
    /// <summary>商品售价，单位为分。</summary>
    public int PriceFen { get; }

    /// <summary>每件作品可免费请求生成图片的总次数。</summary>
    public int FreeImageAttempts { get; }

    /// <summary>兼容原 Go 后端的环境参数及非正整数回退规则。</summary>
    public MiniappBusinessSettings(IConfiguration configuration)
    {
        PriceFen = PositiveIntOrDefault(configuration["PRODUCT_PRICE_FEN"], 12900);
        FreeImageAttempts = PositiveIntOrDefault(configuration["FREE_IMAGE_ATTEMPTS"], 2);
    }

    private static int PositiveIntOrDefault(string? value, int fallback)
        => int.TryParse(value, out var result) && result > 0 ? result : fallback;
}
