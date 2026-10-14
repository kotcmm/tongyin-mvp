using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Application;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
[ApiController]
public sealed class OrdersController(OrderService service) : ControllerBase
{
    [HttpGet("/api/shipping-address")]
    public Task<IResult> GetSavedAddress() => service.GetSavedAddress(HttpContext);

    [HttpPut("/api/shipping-address")]
    public Task<IResult> SaveAddress() => service.SaveAddress(HttpContext);

    [HttpGet("/api/works/{id}/order")]
    public Task<IResult> GetWorkOrder([FromRoute] string id) => service.GetWorkOrder(id, HttpContext);

    [HttpPost("/api/orders")]
    public Task<IResult> CreateOrder() => service.CreateOrder(HttpContext);

    [HttpGet("/api/orders")]
    public Task<IResult> ListOrders() => service.ListOrders(HttpContext);

    [HttpGet("/api/orders/{id}")]
    public Task<IResult> GetOrder([FromRoute] string id) => service.GetOrder(id, HttpContext);

    [HttpPut("/api/orders/{id}/shipping-address")]
    public Task<IResult> UpdateShippingAddress([FromRoute] string id) => service.UpdateShippingAddress(id, HttpContext);

    [HttpPost("/api/orders/{id}/cancel")]
    public Task<IResult> CancelOrder([FromRoute] string id) => service.CancelOrder(id, HttpContext);

    [HttpGet("/api/orders/{id}/tracking")]
    public Task<IResult> GetTracking([FromRoute] string id) => service.GetTracking(id, HttpContext);
}
