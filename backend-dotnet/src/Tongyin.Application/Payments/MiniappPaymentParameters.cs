using System.Text.Json.Serialization;

namespace Tongyin.Application.Payments;

/// <summary>微信小程序 wx.requestPayment 需要的已签名参数，不包含商户私钥。</summary>
public sealed class MiniappPaymentParameters
{
    /// <summary>Unix 秒级时间戳字符串。</summary>
    [JsonPropertyName("timeStamp")]
    public required string TimeStamp { get; init; }

    /// <summary>本次客户端签名用随机串。</summary>
    [JsonPropertyName("nonceStr")]
    public required string NonceStr { get; init; }

    /// <summary>prepay_id=xxx 形式的支付包。</summary>
    [JsonPropertyName("package")]
    public required string Package { get; init; }

    /// <summary>微信小程序只允许 RSA 类型。</summary>
    [JsonPropertyName("signType")]
    public string SignType => "RSA";

    /// <summary>RSA-SHA256 签名结果。</summary>
    [JsonPropertyName("paySign")]
    public required string PaySign { get; init; }
}
