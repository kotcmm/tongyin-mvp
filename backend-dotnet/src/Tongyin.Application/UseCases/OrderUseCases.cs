using System.Text.Json.Nodes;
using Tongyin.Api.Core;

namespace Tongyin.Application.UseCases;

// Pure order state changes and persistence; HTTP status mapping stays in API.
public sealed class OrderUseCases(IStore store)
{
    public static bool ShippingValid(JsonObject? o) => OrderRules.ShippingValid(o);

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
