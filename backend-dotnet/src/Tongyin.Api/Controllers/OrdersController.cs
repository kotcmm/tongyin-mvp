using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
/// <summary>
/// 小程序订单、收货地址与物流查询接口。
/// </summary>
[ApiController]
public sealed class OrdersController(OrderService service) : ControllerBase
{
    /// <summary>
    /// 获取用户保存的收货地址。
    /// </summary>
    [HttpGet("/api/shipping-address")]
    public Task<IResult> GetSavedAddress() => service.GetSavedAddress(HttpContext);

    /// <summary>
    /// 保存用户的收货地址。
    /// </summary>
    [HttpPut("/api/shipping-address")]
    public Task<IResult> SaveAddress() => service.SaveAddress(HttpContext);

    /// <summary>
    /// 查询指定作品对应的订单。
    /// </summary>
    [HttpGet("/api/works/{id}/order")]
    public Task<IResult> GetWorkOrder([FromRoute] string id) => service.GetWorkOrder(id, HttpContext);

    /// <summary>
    /// 为已完成模型的作品创建订单。
    /// </summary>
    [HttpPost("/api/orders")]
    public Task<IResult> CreateOrder() => service.CreateOrder(HttpContext);

    /// <summary>
    /// 查询订单列表。
    /// </summary>
    [HttpGet("/api/orders")]
    public Task<IResult> ListOrders() => service.ListOrders(HttpContext);

    /// <summary>
    /// 查询当前用户的指定订单。
    /// </summary>
    [HttpGet("/api/orders/{id}")]
    public Task<IResult> GetOrder([FromRoute] string id) => service.GetOrder(id, HttpContext);

    /// <summary>
    /// 修改待支付订单的收货信息。
    /// </summary>
    [HttpPut("/api/orders/{id}/shipping-address")]
    public Task<IResult> UpdateShippingAddress([FromRoute] string id) => service.UpdateShippingAddress(id, HttpContext);

    /// <summary>
    /// 取消符合条件的待支付订单。
    /// </summary>
    [HttpPost("/api/orders/{id}/cancel")]
    public Task<IResult> CancelOrder([FromRoute] string id) => service.CancelOrder(id, HttpContext);

    /// <summary>
    /// 获取已发货订单的物流信息。
    /// </summary>
    [HttpGet("/api/orders/{id}/tracking")]
    public Task<IResult> GetTracking([FromRoute] string id) => service.GetTracking(id, HttpContext);
}
