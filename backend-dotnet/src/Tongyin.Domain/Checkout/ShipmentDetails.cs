using System.Text.RegularExpressions;

namespace Tongyin.Domain.Checkout;

/// <summary>管理员核实后的物流公司与真实运单信息。</summary>
public sealed class ShipmentDetails
{
    private ShipmentDetails(string carrier, string carrierCode, string tracking)
    {
        Carrier = carrier;
        CarrierCode = carrierCode;
        Tracking = tracking;
    }

    /// <summary>快递公司名称。</summary>
    public string Carrier { get; }

    /// <summary>快递100使用的小写物流公司编码。</summary>
    public string CarrierCode { get; }

    /// <summary>快递公司实际提供的运单号。</summary>
    public string Tracking { get; }

    /// <summary>按照旧 Go 后台的物流字段规则规范化和校验，不生成虚构运单。</summary>
    public static ShipmentDetails Create(string? carrier, string? carrierCode, string? tracking)
    {
        carrier = carrier?.Trim() ?? "";
        carrierCode = carrierCode?.Trim().ToLowerInvariant() ?? "";
        tracking = tracking?.Trim() ?? "";
        if (carrier.EnumerateRunes().Count() is < 2 or > 40
            || !Regex.IsMatch(carrierCode, "^[a-z0-9]{2,30}$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(tracking, "^[A-Za-z0-9_-]{5,40}$", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("请填写快递公司、快递100编码和有效运单号");
        }
        return new ShipmentDetails(carrier, carrierCode, tracking);
    }
}
