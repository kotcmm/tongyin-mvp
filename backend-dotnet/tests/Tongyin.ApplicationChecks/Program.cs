using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Application.UseCases;

// These use cases run without ASP.NET Core, any database server or external network.
var store=new MemoryStore();
var works=new WorkUseCases(store);
var orders=new OrderUseCases(store);

static void Must(bool value,string why)
{
    if (!value)throw new Exception("FAIL: "+why);
}
static async Task Throws<T>(Func<Task> action,string why) where T:Exception
{
    try {await action();}
    catch(T){return;}
    throw new Exception("FAIL: "+why);
}
var work=await works.Create("user1","drawing.png");
var wid=Doc.Str(work,"id");
Must(Doc.Str(work,"status")=="image_pending" && Doc.Str(work,"owner")=="user1","create work");
await Throws<KeyNotFoundException>(()=>works.QueueImage("user2",wid,2),"cross-owner work guard");
var queued=await works.QueueImage("user1",wid,2);
Must(Doc.Int(queued,"image_attempts")==1 && Doc.Str(queued,"generation_id")!="","queue image and generation");
await Throws<InvalidOperationException>(()=>works.QueueImage("user1",wid,2),"duplicate image request guard");
await store.Update(s=> {
    s.Works[wid]["status"]="image_ready";
    s.Works[wid]["candidates"]=new JsonArray("image.png");
    return true;
});
await Throws<ArgumentException>(()=>works.SelectImage("user1",wid,1),"only first image can be selected");
var selected=await works.SelectImage("user1",wid,0);
Must(Doc.Str(selected,"status")=="model_pending" && Doc.Int(selected,"selected")==0,"select queues model");
await store.Update(s=>{s.Works[wid]["status"]="model_ready";s.Works[wid]["model"]="ready.glb";return true;});

var shipping=new JsonObject{
    ["work_id"]=wid,["name"]="Test Parent",["phone"]="13800138000",["address"]="Guangzhou Tianhe District 100"
};
Must(OrderUseCases.ShippingValid(shipping),"address validation");
var created=await orders.Create("user1",shipping,12900);
var oid=Doc.Str(created,"id");
Must(Doc.Str(created,"status")=="awaiting_payment" && Doc.Int(created,"price_fen")==12900,"order created");
await Throws<InvalidOperationException>(()=>orders.Create("user1",shipping,12900),"duplicate order guard");
await Throws<KeyNotFoundException>(()=>orders.Cancel("user2",oid,"not mine"),"cross-owner order cancellation");
await orders.Cancel("user1",oid,"cancel for now");
var canceled=await store.Read(s=>Doc.Str(s.Orders[oid],"status")=="canceled" && s.Audit.Values.Any(a=>Doc.Str(a,"target")==oid));
Must(canceled,"cancel updates state and audit");
var reordered=await orders.Create("user1",shipping,12900);
Must(Doc.Str(reordered,"id")!=oid,"cancelled order can be recreated");

Console.WriteLine("PASS: framework-free work/order use cases, ownership, state transitions, idempotency checks and audit.");

file sealed class MemoryStore : IStore
{
    private readonly Snapshot state=new();
    public Task<T> Read<T>(Func<Snapshot,T> action,CancellationToken ct=default) => Task.FromResult(action(state));
    public Task<T> Update<T>(Func<Snapshot,T> action,CancellationToken ct=default) => Task.FromResult(action(state));
}
