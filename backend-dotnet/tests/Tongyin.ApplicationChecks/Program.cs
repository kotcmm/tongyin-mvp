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
Must((await works.List("user1")).Length==1 && (await works.List("user2")).Length==0,"only owner lists works");
Must((await works.Find("user2",wid))==null && (await works.Find("user1",wid))!=null,
    "only owner finds work");
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
var byWork=await orders.GetWorkOrder("user1",wid);
Must(byWork.Found && Doc.Str(byWork.Order,"id")==oid,"find order for work");
Must(!(await orders.GetWorkOrder("user2",wid)).Found,"other user cannot query work's order");
Must((await orders.List("user2")).Length==0 && (await orders.List("user1")).Length==1,
    "owner-only orders list");
Must((await orders.Find("user2",oid))==null,"owner-only order detail");
var savedAddress=await orders.GetSavedAddress("user1");
Must(Doc.Str(savedAddress,"owner")=="" && Doc.Str(savedAddress,"name")=="Test Parent",
    "address stored and owner is masked");
var newAddress=new JsonObject{
    ["work_id"]=wid,["name"]="Updated Parent",["phone"]="13800138000",["address"]="Guangzhou Yuexiu District 200"
};
var edited=await orders.UpdateShippingAddress("user1",oid,newAddress);
Must(Doc.Str(edited,"name")=="Updated Parent","edit unpaid order address");
await Throws<KeyNotFoundException>(()=>orders.UpdateShippingAddress("user2",oid,newAddress),
    "cross-owner address update guard");
await Throws<InvalidOperationException>(()=>orders.Create("user1",shipping,12900),"duplicate order guard");
await Throws<KeyNotFoundException>(()=>orders.Cancel("user2",oid,"not mine"),"cross-owner order cancellation");
await orders.Cancel("user1",oid,"cancel for now");
await Throws<InvalidOperationException>(()=>orders.UpdateShippingAddress("user1",oid,new JsonObject{
    ["name"]="Another Parent",["phone"]="13800138000",["address"]="Guangzhou Tianhe District 100"
}),"address cannot be changed after cancellation");
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
