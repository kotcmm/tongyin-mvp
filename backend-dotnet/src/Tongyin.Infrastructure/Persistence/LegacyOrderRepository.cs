using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Application.Orders;
using Tongyin.Domain.Orders;
using Tongyin.Domain.Works;

namespace Tongyin.Infrastructure.Persistence;

public sealed class LegacyOrderRepository(IStore store) : IOrderRepository
{
    public Task<Order> PlaceAsync(Order order,CancellationToken ct=default) => store.Update(s=>
    {
        var state=order.Snapshot();
        var readyWork=s.Works.TryGetValue(state.WorkId,out var doc)
            && Doc.Str(doc,"owner")==state.Owner
            && LegacyWorkDocument.ToDomain(doc).Snapshot().Status==WorkStatus.ModelReady;
        var active=s.Orders.Values.Any(x=>
            Doc.Str(x,"owner")==state.Owner && Doc.Str(x,"work_id")==state.WorkId
            && Doc.Str(x,"status")!="canceled");
        Order.EnsurePlaceable(readyWork,active);
        if(s.Orders.ContainsKey(state.Id))
            throw new InvalidOperationException("订单编号重复");
        s.Orders[state.Id]=LegacyOrderDocument.ToDocument(state);
        s.Addresses[state.Owner]=new JsonObject {
            ["owner"]=state.Owner,["created_at"]=Doc.Now(),
            ["name"]=state.Recipient.Name,["phone"]=state.Recipient.Phone,
            ["address"]=state.Recipient.Address
        };
        return order;
    },ct);

    public Task<Order?> FindAsync(string owner,string id,CancellationToken ct=default) =>
        store.Read(s=>s.Orders.TryGetValue(id,out var doc) && Doc.Str(doc,"owner")==owner
            ? LegacyOrderDocument.ToDomain(doc):null,ct);

    public Task<Order> UpdateAsync(string owner,string id,Action<Order> change,CancellationToken ct=default) =>
        store.Update(s=>
        {
            if(!s.Orders.TryGetValue(id,out var original) || Doc.Str(original,"owner")!=owner)
                throw new KeyNotFoundException("订单不存在");
            var order=LegacyOrderDocument.ToDomain(original);
            var previous=order.Snapshot().Status;
            change(order);
            var state=order.Snapshot();
            s.Orders[id]=LegacyOrderDocument.ToDocument(state,original);
            if(previous!=OrderStatus.Canceled && state.Status==OrderStatus.Canceled)
            {
                var auditId=Doc.NewId();
                s.Audit[auditId]=new JsonObject {
                    ["id"]=auditId,["owner"]="customer",["created_at"]=Doc.Now(),
                    ["action"]="order.canceled",["target"]=id,["detail"]=""
                };
            }
            return order;
        },ct);
}

public sealed class GoCompatibleOrderIdsAndTime : IOrderIdsAndTime
{
    public string NewId()=>Doc.NewId();
    public string Now()=>Doc.Now();
}
