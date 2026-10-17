using Tongyin.Domain.Checkout;
using Xunit;

namespace Tongyin.Domain.Tests.Checkout;

/// <summary>验证收货信息的领域校验与规范化规则，对齐原 Go MVP。</summary>
public sealed class ShippingDetailsTests
{
    /// <summary>合法收货信息去除首尾空白，不改变用户填写的具体地址内容。</summary>
    [Fact]
    public void Create_ValidShipping_Trimmed()
    {
        var address = ShippingDetails.Create(" 张三 ", " 13800138000 ", " 广州市海珠区新港东路100号 ");
        Assert.Equal("张三", address.Name);
        Assert.Equal("13800138000", address.Phone);
        Assert.Equal("广州市海珠区新港东路100号", address.Address);
    }

    /// <summary>姓名、电话或地址不完整时禁止保存或创建订单。</summary>
    [Theory]
    [InlineData("张", "13800138000", "广州市海珠区新港东路100号")]
    [InlineData("张三", "123", "广州市海珠区新港东路100号")]
    [InlineData("张三", "13800138000", "短地址")]
    [InlineData("", "13800138000", "广州市海珠区新港东路100号")]
    public void Create_InvalidShipping_Rejects(string name, string phone, string address)
    {
        Assert.Throws<ArgumentException>(() => ShippingDetails.Create(name, phone, address));
    }

    /// <summary>超出旧后端文字长度上限的内容不能存入个人地址。</summary>
    [Fact]
    public void Create_TooLongAddress_Rejects()
    {
        Assert.Throws<ArgumentException>(() => ShippingDetails.Create("张三", "13800138000", new string('中', 301)));
    }
}
