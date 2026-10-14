using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
[ApiController]
public sealed class PaymentController(PaymentService service) : ControllerBase
{
    [HttpPost("/api/orders/{id}/pay")]
    public Task<IResult> StartPayment([FromRoute] string id) => service.StartPayment(id, HttpContext);

    [HttpPost("/api/pay/notify")]
    public Task<IResult> NotifyPayment() => service.NotifyPayment(HttpContext);
}
