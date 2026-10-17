namespace Tongyin.Domain.Checkout;

/// <summary>用户确认的收货人、电话和详细地址，按照旧 Go 下单规则校验并去除首尾空格。</summary>
public sealed class ShippingDetails
{
    private ShippingDetails(string name, string phone, string address)
    {
        Name = name;
        Phone = phone;
        Address = address;
    }

    /// <summary>收货人姓名。</summary>
    public string Name { get; }

    /// <summary>联系手机或其他可接收快递通知的电话。</summary>
    public string Phone { get; }

    /// <summary>包含省市区及门牌的详细收货地址。</summary>
    public string Address { get; }

    /// <summary>校验内容完整性并规范化；中文字符按 Unicode 标量计数。</summary>
    public static ShippingDetails Create(string? name, string? phone, string? address)
    {
        name = name?.Trim() ?? "";
        phone = phone?.Trim() ?? "";
        address = address?.Trim() ?? "";
        var nameLength = name.EnumerateRunes().Count();
        var addressLength = address.EnumerateRunes().Count();
        if (nameLength is < 2 or > 30 || phone.Length is < 6 or > 20 || addressLength is < 8 or > 300)
        {
            throw new ArgumentException("请填写完整收货信息");
        }
        return new ShippingDetails(name, phone, address);
    }
}
