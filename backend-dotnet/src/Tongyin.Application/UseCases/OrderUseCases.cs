using System.Text.Json.Nodes;
using Tongyin.Api.Core;

namespace Tongyin.Application.UseCases;

// Pure order state changes and persistence; HTTP status mapping stays in API.
public sealed class OrderUseCases(IStore store)
{
    public static bool ShippingValid(JsonObject? o) => OrderRules.ShippingValid(o);

    private static JsonObject ExposeAddress(JsonObject address)
    {
        var copy=Doc.Copy(address);
        copy["owner"]="";
        return copy;
    }

    public Task<JsonObject?> GetSavedAddress(string owner) => store.Read(s=>
    {
        if(s.Addresses.TryGetValue(owner,out var a))return ExposeAddress(a);
        var prev=s.Orders.Values.Where(o=>Doc.Str(o,"owner")==owner && Doc.Str(o,"name")!="" && Doc.Str(o,"address")!="")
            .OrderByDescending(o=>Doc.Str(o,"created_at")).FirstOrDefault();
        return prev==null ? null : new JsonObject{
            ["created_at"]=Doc.Str(prev,"created_at"),["name"]=Doc.Str(prev,"name"),
            ["phone"]=Doc.Str(prev,"phone"),["address"]=Doc.Str(prev,"address"),["owner"]=""
        };
    });

    public async Task<JsonObject> SaveAddress(string owner,JsonObject body)
    {
        var addr=new JsonObject{
            ["owner"]=owner,["created_at"]=Doc.Now(),["name"]=Doc.Str(body,"name").Trim(),
            ["phone"]=Doc.Str(body,"phone").Trim(),["address"]=Doc.Str(body,"address").Trim()
        };
        await store.Update(s=>{s.Addresses[owner]=addr;return true;});
        return ExposeAddress(addr);
    }

    public Task<(bool Found,JsonObject? Order)> GetWorkOrder(string owner,string workId) => store.Read(s=>
    {
        if(!s.Works.TryGetValue(workId,out var work) || Doc.Str(work,"owner")!=owner)
            return (Found:false,Order:(JsonObject?)null);
        var order=s.Orders.Values.Where(o=>Doc.Str(o,"work_id")==workId && Doc.Str(o,"owner")==owner && Doc.Str(o,"status")!="canceled")
            .OrderByDescending(o=>Doc.Str(o,"created_at")).FirstOrDefault();
        return (Found:true,Order:order==null ? null : Doc.Copy(order));
    });

    public Task<JsonObject[]> List(string owner) => store.Read(s=>
        s.Orders.Values.Where(o=>Doc.Str(o,"owner")==owner)
            .OrderByDescending(o=>Doc.Str(o,"created_at")).Select(Doc.Copy).ToArray());

    public Task<JsonObject?> Find(string owner,string id) => store.Read(s=>
        s.Orders.TryGetValue(id,out var o) && Doc.Str(o,"owner")==owner
            ? Doc.Copy(o) : null);

    public Task<JsonObject> UpdateShippingAddress(string owner,string id,JsonObject body) => store.Update(s=>
    {
        if(!s.Orders.TryGetValue(id,out var order) || Doc.Str(order,"owner")!=owner)
            throw new KeyNotFoundException();
        if(Doc.Str(order,"status")!="awaiting_payment")
            throw new InvalidOperationException("只有待付款订单可以修改收货地址");
        order["name"]=Doc.Str(body,"name").Trim();
        order["phone"]=Doc.Str(body,"phone").Trim();
        order["address"]=Doc.Str(body,"address").Trim();
        return Doc.Copy(order);
    });

    public async Task<JsonObject> Create(string owner,JsonObject body,int priceFen)
    {
        var workId=Doc.Str(body,"work_id");
        var order=new JsonObject {
            ["id"]=Doc.NewId(),["owner"]=owner,["work_id"]=workId,["created_at"]=Doc.Now(),
            ["name"]=Doc.Str(body,"name").Trim(),["phone"]=Doc.Str(body,"phone").Trim(),
            ["address"]=Doc.Str(body,"address").Trim(),["price_fen"]=priceFen,
            ["status"]="awaiting_payment",
            ["history"]=new JsonArray(new JsonObject{["at"]=Doc.Now(),["actor"]="customer",["action"]="created"})
        };
        await store.Update(s=>
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
        return order;
    }

    public Task Cancel(string owner,string id,string reason) => store.Update(s=>
    {
        if(!s.Orders.TryGetValue(id,out var o) || Doc.Str(o,"owner")!=owner)throw new KeyNotFoundException();
        OrderRules.RequireCancelable(o);
        o["status"]="canceled";o["cancel_reason"]=reason;
        if(o["history"] is not JsonArray)o["history"]=new JsonArray();
        ((JsonArray)o["history"]!).Add(new JsonObject{
            ["at"]=Doc.Now(),["actor"]="customer",["action"]="canceled",["note"]=reason});
        var audit=new JsonObject {
            ["id"]=Doc.NewId(),["owner"]="customer",["created_at"]=Doc.Now(),
            ["action"]="order.canceled",["target"]=id,["detail"]=""
        };
        s.Audit[Doc.Str(audit,"id")]=audit;
        return true;
    });
}
