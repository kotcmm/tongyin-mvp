using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Api.ViewModels.Checkout;
using Tongyin.Application.Checkout;

namespace Tongyin.Api.Controllers;

/// <summary>微信用户自己的默认收货地址，支持首次填写和后续更新。</summary>
[ApiController]
[Route("api/shipping-address")]
[WechatAuthorize]
public sealed class ShippingAddressController(CheckoutService checkout) : ControllerBase
{
    /// <summary>读取默认地址；没有时返回 `{"address":null}` 以兼容小程序。</summary>
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
        => Ok(new { address = await checkout.GetShippingAsync(User.GetWechatOpenId(), cancellationToken) });

    /// <summary>强类型绑定并校验收货信息后保存到当前微信用户。</summary>
    [HttpPut]
    public async Task<IActionResult> PutAsync([FromBody] ShippingRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await checkout.SaveShippingAsync(
                User.GetWechatOpenId(), request.Name, request.Phone, request.Address, cancellationToken);
            return Ok(new { address = result });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
