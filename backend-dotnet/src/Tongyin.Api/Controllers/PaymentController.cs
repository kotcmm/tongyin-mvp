using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
/// <summary>
/// 微信支付下单和支付回调接口。
/// </summary>
[ApiController]
public sealed class PaymentController(PaymentService service) : ControllerBase
{
    /// <summary>
    /// 为待支付订单发起微信支付。
    /// </summary>
    [HttpPost("/api/orders/{id}/pay")]
    public Task<IResult> StartPayment([FromRoute] string id) => service.StartPayment(id, HttpContext);

    /// <summary>
    /// 接收并验证微信支付异步通知。
    /// </summary>
    [HttpPost("/api/pay/notify")]
    public Task<IResult> NotifyPayment() => service.NotifyPayment(HttpContext);
}
