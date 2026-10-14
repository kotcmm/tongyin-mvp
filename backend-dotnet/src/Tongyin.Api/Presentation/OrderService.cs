using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Application.Orders;
using Tongyin.Infrastructure.Persistence;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Presentation;

/// <summary>
/// 订单查询、地址管理与业务操作的过渡 HTTP 适配服务。
/// </summary>
public sealed class OrderService(CompatAuth auth, IStore db, Settings cfg, OrderUseCases orders)
{
    private static bool ShippingValid(JsonObject? o)
    {
        var name=Doc.Str(o,"name").Trim();
        var phone=Doc.Str(o,"phone").Trim();
        var address=Doc.Str(o,"address").Trim();
        return name.EnumerateRunes().Count() is >= 2 and <= 30 && phone.Length is >= 6 and <= 20 &&
               address.EnumerateRunes().Count() is >= 8 and <= 300;
    }
    private static JsonObject CopyAddress(JsonObject address)
    {
        var cp=Doc.Copy(address);
        cp["owner"]="";
        return cp;
    }

    // GET /api/shipping-address
    /// <summary>
    /// 查找用户保存或最近使用的收货地址。
    /// </summary>
    public async Task<IResult> GetSavedAddress(HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var v=await db.Read(s => {
                if(s.Addresses.TryGetValue(owner,out var a))return CopyAddress(a);
                var prev=s.Orders.Values.Where(o=>Doc.Str(o,"owner")==owner && Doc.Str(o,"name")!="" && Doc.Str(o,"address")!="")
                    .OrderByDescending(o=>Doc.Str(o,"created_at")).FirstOrDefault();
                return prev==null ? null : new JsonObject{
                    ["created_at"]=Doc.Str(prev,"created_at"),["name"]=Doc.Str(prev,"name"),
                    ["phone"]=Doc.Str(prev,"phone"),["address"]=Doc.Str(prev,"address"),["owner"]=""
                };
            });
            return Ok(new {address=v});
    }

    // PUT /api/shipping-address
    /// <summary>
    /// 保存用户收货信息。
    /// </summary>
    public async Task<IResult> SaveAddress(HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);if(body==null || !ShippingValid(body))return Fail(400,"请填写完整收货信息");
            var addr=new JsonObject{
                ["owner"]=owner,["created_at"]=Doc.Now(),["name"]=Doc.Str(body,"name").Trim(),
                ["phone"]=Doc.Str(body,"phone").Trim(),["address"]=Doc.Str(body,"address").Trim()
            };
            await db.Update(s=>{s.Addresses[owner]=addr;return true;});
            return Ok(new {address=CopyAddress(addr)});
    }

    // GET /api/works/{id}/order
    /// <summary>
    /// 查询作品最近一个有效订单。
    /// </summary>
    public async Task<IResult> GetWorkOrder(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var result=await db.Read(s=>
            {
                if(!s.Works.TryGetValue(id,out var w) || Doc.Str(w,"owner")!=owner)return (Found:false,Order:(JsonObject?)null);
                var order=s.Orders.Values.Where(o=>Doc.Str(o,"work_id")==id && Doc.Str(o,"owner")==owner && Doc.Str(o,"status")!="canceled")
                    .OrderByDescending(o=>Doc.Str(o,"created_at")).FirstOrDefault();
                return (Found:true,Order:order==null ? null : ExposeOrder(order));
            });
            return !result.Found ? Fail(404,"作品不存在") : Ok(new {order=result.Order});
    }

    // POST /api/orders
    /// <summary>
    /// 校验作品和收货信息并创建订单。
    /// </summary>
    public async Task<IResult> CreateOrder(HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);
            if(body==null)return Fail(400,"参数有误");
            if(!ShippingValid(body))return Fail(400,"请填写完整收货信息");
            try
            {
                var created=await orders.PlaceAsync(new PlaceOrderCommand(
                    owner,Doc.Str(body,"work_id"),Doc.Str(body,"name"),
                    Doc.Str(body,"phone"),Doc.Str(body,"address"),cfg.PriceFen));
                return Ok(ExposeOrder(LegacyOrderDocument.ToDocument(created)),201);
            }
            catch(Exception e){return Fail(409,e.Message);}
    }

    // GET /api/orders
    /// <summary>
    /// 查询后台订单列表。
    /// </summary>
    public async Task<IResult> ListOrders(HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var orders=await db.Read(s=>s.Orders.Values.Where(o=>Doc.Str(o,"owner")==owner)
                .OrderByDescending(o=>Doc.Str(o,"created_at")).Select(ExposeOrder).ToArray());
            return Ok(orders);
    }

    // GET /api/orders/{id}
    /// <summary>
    /// 查找属于当前用户的订单。
    /// </summary>
    public async Task<IResult> GetOrder(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var order=await db.Read(s=>s.Orders.TryGetValue(id,out var o) && Doc.Str(o,"owner")==owner ? ExposeOrder(o):null);
            return order==null ? Fail(404,"订单不存在") : Ok(order);
    }

    // PUT /api/orders/{id}/shipping-address
    /// <summary>
    /// 修改尚未付款订单的收货信息。
    /// </summary>
    public async Task<IResult> UpdateShippingAddress(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);
            if(body==null || !ShippingValid(body))return Fail(400,"请填写完整收货信息");
            try
            {
                var updated=await db.Update(s=>
                {
                    if(!s.Orders.TryGetValue(id,out var order) || Doc.Str(order,"owner")!=owner)throw new KeyNotFoundException();
                    if(Doc.Str(order,"status")!="awaiting_payment")throw new InvalidOperationException("只有待付款订单可以修改收货地址");
                    order["name"]=Doc.Str(body,"name").Trim();
                    order["phone"]=Doc.Str(body,"phone").Trim();
                    order["address"]=Doc.Str(body,"address").Trim();
                    return ExposeOrder(order);
                });
                return Ok(updated);
            }
            catch(KeyNotFoundException){return Fail(404,"订单不存在");}
            catch(Exception e){return Fail(409,e.Message);}
    }

    // POST /api/orders/{id}/cancel
    /// <summary>
    /// 取消未支付且尚未进入支付流程的订单。
    /// </summary>
    public async Task<IResult> CancelOrder(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);if(body==null)return Fail(400,"参数错误");
            var reason=Doc.Str(body,"reason").Trim();
            if(reason.EnumerateRunes().Count()>200)return Fail(400,"取消原因太长");
            try
            {
                await orders.CancelAsync(owner,id,reason);
                return Ok(new {status="canceled"});
            }
            catch(KeyNotFoundException){return Fail(404,"订单不存在");}
            catch(Exception e){return Fail(409,e.Message);}
    }

    // GET /api/orders/{id}/tracking
    /// <summary>
    /// 读取已发货订单的追踪轨迹。
    /// </summary>
    public async Task<IResult> GetTracking(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var order=await db.Read(s=>s.Orders.TryGetValue(id,out var o) && Doc.Str(o,"owner")==owner ? Doc.Copy(o):null);
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
