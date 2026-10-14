using System.Text;
namespace Tongyin.Domain.Orders;

/// <summary>
/// 收货信息值对象，创建时验证联系人、电话和详细地址。
/// </summary>
public sealed record ShippingAddress
{
    /// <summary>
    /// 收件人姓名。
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// 收件人联系电话。
    /// </summary>
    public string Phone { get; }
    /// <summary>
    /// 详细收货地址。
    /// </summary>
    public string Address { get; }
    /// <summary>
    /// 收货信息值对象，创建时验证联系人、电话和详细地址。
    /// </summary>
    public ShippingAddress(string name,string phone,string address)
    {
        Name=name.Trim(); Phone=phone.Trim(); Address=address.Trim();
        if(Name.EnumerateRunes().Count() is <2 or >30 ||
           Phone.Length is <6 or >20 ||
           Address.EnumerateRunes().Count() is <8 or >300)
            throw new ArgumentException("请填写完整收货信息");
    }
}
