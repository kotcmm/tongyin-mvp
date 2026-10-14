using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Domain.Orders;

namespace Tongyin.Infrastructure.Persistence;

// Go ty_orders payload translation; unknown fields and existing event attributes survive.
public static class LegacyOrderDocument
{
    private static OrderStatus Status(string s)=>s switch
    {
        "awaiting_payment"=>OrderStatus.AwaitingPayment,"paid"=>OrderStatus.Paid,
        "printing"=>OrderStatus.Printing,"shipped"=>OrderStatus.Shipped,
        "completed"=>OrderStatus.Completed,"canceled"=>OrderStatus.Canceled,
        _=>OrderStatus.Unknown
    };
    private static string Text(OrderStatus s)=>s switch
    {
        OrderStatus.AwaitingPayment=>"awaiting_payment",OrderStatus.Paid=>"paid",
        OrderStatus.Printing=>"printing",OrderStatus.Shipped=>"shipped",
        OrderStatus.Completed=>"completed",OrderStatus.Canceled=>"canceled",
        _=>throw new InvalidOperationException("不能写入未知订单状态")
    };

    /// <summary>
    /// 将 Go 数据文档映射为领域聚合。
    /// </summary>
    public static Order ToDomain(JsonObject raw)
    {
        var events=Doc.Array(raw,"history").Select(e=>new OrderEvent(
            Doc.Str(e,"at"),Doc.Str(e,"actor"),Doc.Str(e,"action"),Doc.Str(e,"note"))).ToArray();
        var address=new ShippingAddress(Doc.Str(raw,"name"),Doc.Str(raw,"phone"),Doc.Str(raw,"address"));
        return Order.Restore(new OrderState(
            Doc.Str(raw,"id"),Doc.Str(raw,"owner"),Doc.Str(raw,"work_id"),
            Doc.Str(raw,"created_at"),address,Doc.Int(raw,"price_fen"),
            Status(Doc.Str(raw,"status")),Doc.Str(raw,"payment_started_at"),
            Doc.Str(raw,"cancel_reason"),events));
    }

    private static JsonObject AsJson(OrderEvent e)
    {
        var raw=new JsonObject {
            ["at"]=e.At,["actor"]=e.Actor,["action"]=e.Action
        };
        if(e.Note!="")raw["note"]=e.Note;
        return raw;
    }

    /// <summary>
    /// 将领域状态写回旧版数据格式，并保留未知字段。
    /// </summary>
    public static JsonObject ToDocument(OrderState state,JsonObject? previous=null)
    {
        var raw=previous is null?new JsonObject():Doc.Copy(previous);
        raw["id"]=state.Id;raw["owner"]=state.Owner;raw["work_id"]=state.WorkId;
        raw["created_at"]=state.CreatedAt;raw["name"]=state.Recipient.Name;
        raw["phone"]=state.Recipient.Phone;raw["address"]=state.Recipient.Address;
        raw["price_fen"]=state.PriceFen;raw["status"]=Text(state.Status);

        // Existing history entries may contain unknown Go properties: retain them.
        var events=Doc.Array(raw,"history");
        for(var i=events.Count;i<state.History.Count;i++)events.Add(AsJson(state.History[i]));
        raw["history"]=events.DeepClone();
        if(state.CancelReason!="" || raw.ContainsKey("cancel_reason"))
            raw["cancel_reason"]=state.CancelReason;
        return raw;
    }
}
