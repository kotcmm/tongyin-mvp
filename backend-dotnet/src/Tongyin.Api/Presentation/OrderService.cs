using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Application.UseCases;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Presentation;

public sealed class OrderService(CompatAuth auth, Settings cfg, OrderUseCases useCases)
{
    // GET /api/shipping-address
    public async Task<IResult> GetSavedAddress(HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var v=await useCases.GetSavedAddress(owner);
            return Ok(new {address=v});
    }

    // PUT /api/shipping-address
    public async Task<IResult> SaveAddress(HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);if(body==null || !OrderUseCases.ShippingValid(body))return Fail(400,"请填写完整收货信息");
            var addr=await useCases.SaveAddress(owner,body);
            return Ok(new {address=addr});
    }

    // GET /api/works/{id}/order
    public async Task<IResult> GetWorkOrder(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var result=await useCases.GetWorkOrder(owner,id);
            return !result.Found ? Fail(404,"作品不存在") :
                Ok(new {order=result.Order==null ? null : ExposeOrder(result.Order)});
    }

    // POST /api/orders
    public async Task<IResult> CreateOrder(HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);
            if(body==null)return Fail(400,"参数有误");
            if(!OrderUseCases.ShippingValid(body))return Fail(400,"请填写完整收货信息");
            try
            {
                var created=await useCases.Create(owner,body,cfg.PriceFen);
                return Ok(ExposeOrder(created),201);
            }
            catch(Exception e){return Fail(409,e.Message);}
    }

    // GET /api/orders
    public async Task<IResult> ListOrders(HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var orders=await useCases.List(owner);
            return Ok(orders.Select(ExposeOrder).ToArray());
    }

    // GET /api/orders/{id}
    public async Task<IResult> GetOrder(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var order=await useCases.Find(owner,id);
            return order==null ? Fail(404,"订单不存在") : Ok(ExposeOrder(order));
    }

    // PUT /api/orders/{id}/shipping-address
    public async Task<IResult> UpdateShippingAddress(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);
            if(body==null || !OrderUseCases.ShippingValid(body))return Fail(400,"请填写完整收货信息");
            try
            {
                var updated=await useCases.UpdateShippingAddress(owner,id,body);
                return Ok(ExposeOrder(updated));
            }
            catch(KeyNotFoundException){return Fail(404,"订单不存在");}
            catch(Exception e){return Fail(409,e.Message);}
    }

    // POST /api/orders/{id}/cancel
    public async Task<IResult> CancelOrder(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);if(body==null)return Fail(400,"参数错误");
            var reason=Doc.Str(body,"reason").Trim();
            if(reason.EnumerateRunes().Count()>200)return Fail(400,"取消原因太长");
            try
            {
                await useCases.Cancel(owner,id,reason);
                return Ok(new {status="canceled"});
            }
            catch(KeyNotFoundException){return Fail(404,"订单不存在");}
            catch(Exception e){return Fail(409,e.Message);}
    }

    // GET /api/orders/{id}/tracking
    public async Task<IResult> GetTracking(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var order=await useCases.Find(owner,id);
            if(order==null)return Fail(404,"订单不存在");
            if(Doc.Str(order,"status") is not ("shipped" or "completed"))return Fail(409,"订单尚未发货");
            var events=Doc.Array(order,"tracking_events");
            var source=events.Count==0 ? "unavailable" : events.Any(v=>Doc.Str(v,"source")=="kuaidi100") ? "kuaidi100" : "manual";
            return Ok(new {
                company=Doc.Str(order,"carrier"),company_code=Doc.Str(order,"carrier_code"),
                tracking_no=Doc.Str(order,"tracking"),tracking_state=Doc.Str(order,"tracking_state"),
                updated_at=Doc.Str(order,"tracking_checked_at"),events,source
            });
    }

}
