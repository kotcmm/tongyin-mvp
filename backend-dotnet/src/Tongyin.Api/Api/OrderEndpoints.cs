using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Api;

public static class OrderEndpoints
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
    public static void MapOrders(this WebApplication app)
    {
        app.MapGet("/api/shipping-address", async (HttpContext ctx,CompatAuth auth,IStore db) =>
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
        });
        app.MapPut("/api/shipping-address", async (HttpContext ctx,CompatAuth auth,IStore db) =>
        {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);if(body==null || !ShippingValid(body))return Fail(400,"请填写完整收货信息");
            var addr=new JsonObject{
                ["owner"]=owner,["created_at"]=Doc.Now(),["name"]=Doc.Str(body,"name").Trim(),
                ["phone"]=Doc.Str(body,"phone").Trim(),["address"]=Doc.Str(body,"address").Trim()
            };
            await db.Update(s=>{s.Addresses[owner]=addr;return true;});
            return Ok(new {address=CopyAddress(addr)});
        });
        app.MapGet("/api/works/{id}/order",async (string id,HttpContext ctx,CompatAuth auth,IStore db)=>
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
        });
        app.MapPost("/api/orders",async (HttpContext ctx,CompatAuth auth,IStore db,Settings cfg)=>
        {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);
            if(body==null)return Fail(400,"参数有误");
            if(!ShippingValid(body))return Fail(400,"请填写完整收货信息");
            var workId=Doc.Str(body,"work_id");
            var order=new JsonObject {
                ["id"]=Doc.NewId(),["owner"]=owner,["work_id"]=workId,["created_at"]=Doc.Now(),
                ["name"]=Doc.Str(body,"name").Trim(),["phone"]=Doc.Str(body,"phone").Trim(),
                ["address"]=Doc.Str(body,"address").Trim(),["price_fen"]=cfg.PriceFen,
                ["status"]="awaiting_payment",
                ["history"]=new JsonArray(new JsonObject{["at"]=Doc.Now(),["actor"]="customer",["action"]="created"})
            };
            try
            {
                await db.Update(s=>
                {
                    if(!s.Works.TryGetValue(workId,out var w) || Doc.Str(w,"owner")!=owner || Doc.Str(w,"status")!="model_ready")
                        throw new InvalidOperationException("模型尚未准备好");
                    if(s.Orders.Values.Any(o=>Doc.Str(o,"work_id")==workId && Doc.Str(o,"owner")==owner && Doc.Str(o,"status")!="canceled"))
                        throw new InvalidOperationException("该作品已有订单");
                    s.Orders[Doc.Str(order,"id")]=order;
                    s.Addresses[owner]=new JsonObject{
                        ["owner"]=owner,["created_at"]=Doc.Now(),
                        ["name"]=Doc.Str(order,"name"),["phone"]=Doc.Str(order,"phone"),["address"]=Doc.Str(order,"address")
                    };
                    return true;
                });
                return Ok(ExposeOrder(order),201);
            }
            catch(Exception e){return Fail(409,e.Message);}
        });
        app.MapGet("/api/orders",async (HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var orders=await db.Read(s=>s.Orders.Values.Where(o=>Doc.Str(o,"owner")==owner)
                .OrderByDescending(o=>Doc.Str(o,"created_at")).Select(ExposeOrder).ToArray());
            return Ok(orders);
        });
        app.MapGet("/api/orders/{id}",async (string id,HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var order=await db.Read(s=>s.Orders.TryGetValue(id,out var o) && Doc.Str(o,"owner")==owner ? ExposeOrder(o):null);
            return order==null ? Fail(404,"订单不存在") : Ok(order);
        });
        app.MapPut("/api/orders/{id}/shipping-address",async (string id,HttpContext ctx,CompatAuth auth,IStore db)=>
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
        });
        app.MapPost("/api/orders/{id}/cancel",async (string id,HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);if(body==null)return Fail(400,"参数错误");
            var reason=Doc.Str(body,"reason").Trim();
            if(reason.EnumerateRunes().Count()>200)return Fail(400,"取消原因太长");
            try
            {
                await db.Update(s=>
                {
                    if(!s.Orders.TryGetValue(id,out var o) || Doc.Str(o,"owner")!=owner)throw new KeyNotFoundException();
                    if(Doc.Str(o,"status")!="awaiting_payment")
                        throw new InvalidOperationException("只有待付款订单可取消");
                    if(Doc.Str(o,"payment_started_at")!="")
                        throw new InvalidOperationException("该订单已发起微信支付，请等待支付状态确认后联系客服处理取消");
                    o["status"]="canceled";o["cancel_reason"]=reason;
                    if(o["history"] is not JsonArray)o["history"]=new JsonArray();
                    ((JsonArray)o["history"]!).Add(new JsonObject{
                        ["at"]=Doc.Now(),["actor"]="customer",["action"]="canceled",["note"]=reason});
                    Record(s,"customer","order.canceled",id);
                    return true;
                });
                return Ok(new {status="canceled"});
            }
            catch(KeyNotFoundException){return Fail(404,"订单不存在");}
            catch(Exception e){return Fail(409,e.Message);}
        });
        app.MapGet("/api/orders/{id}/tracking",async (string id,HttpContext ctx,CompatAuth auth,IStore db)=>
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
        });
    }
}
