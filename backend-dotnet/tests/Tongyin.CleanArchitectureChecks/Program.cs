using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Tongyin.Infrastructure.Persistence;

static void Must(bool ok,string name)
{
    if(!ok)throw new Exception("FAIL "+name);
}
static async Task Expect<T>(Func<Task> fn,string name) where T:Exception
{
    try {await fn();}
    catch(T){return;}
    throw new Exception("FAIL missing exception "+name);
}

var backing=new TestStore();
var repo=new LegacyWorkRepository(backing);
var gateway=new FakeGeneration();
var useCases=new WorkUseCases(repo,gateway,new GoCompatibleIdsAndTime());

var created=await useCases.CreateAsync("u1","drawing.png");
Must(created.Status==WorkStatus.ImagePending && created.ImageAttempts==0,"new aggregate");
Must(created.Id.Length==24 && created.Selected==-1,"Go id/state contract");
Must((await useCases.ListAsync("u1")).Count==1,"list");
Must((await useCases.ListAsync("u2")).Count==0,"owner list isolation");
Must((await useCases.GetAsync("u2",created.Id)) is null,"owner detail isolation");
await Expect<KeyNotFoundException>(async()=>{await useCases.GenerateAsync("u2",created.Id,2);},"owner write isolation");

await backing.Update(s=>{s.Works[created.Id]["custom_go_field"]="keep-me";return true;});
var queued=await useCases.GenerateAsync("u1",created.Id,2);
Must(queued.Work.Status==WorkStatus.ImagePending && queued.Work.ImageAttempts==1,"generation state");
Must(gateway.Dispatches.SequenceEqual(new[]{"image"}),"image dispatch");
await Expect<InvalidOperationException>(async()=>{await useCases.GenerateAsync("u1",created.Id,2);},"repeated pending");
await backing.Read(s=>{
    Must(Doc.Str(s.Works[created.Id],"custom_go_field")=="keep-me","unknown Go field preserved");
    return true;
});
await backing.Update(s=>{
    s.Works[created.Id]["status"]="image_ready";
    s.Works[created.Id]["candidates"]=new JsonArray("result.png");
    return true;
});
await Expect<ArgumentException>(async()=>{await useCases.SelectAsync("u1",created.Id,2);},"invalid image index");
var selected=await useCases.SelectAsync("u1",created.Id,0);
Must(selected.Work.Status==WorkStatus.ModelPending && selected.Work.Candidates.Count==1,"model state");
Must(gateway.Dispatches.SequenceEqual(new[]{"image","model"}),"model dispatch");
await backing.Read(s=>{
    Must(Doc.Str(s.Works[created.Id],"custom_go_field")=="keep-me","unknown field on select");
    return true;
});
Console.WriteLine("PASS: Domain Work invariants, Application use cases, Repository adapter and Go JSON compatibility.");

sealed class TestStore : IStore
{
    private Snapshot state=new();
    public Task<T> Read<T>(Func<Snapshot,T> fn,CancellationToken ct=default) =>
        Task.FromResult(fn(state.DeepClone()));
    public Task<T> Update<T>(Func<Snapshot,T> fn,CancellationToken ct=default)
    {
        var copy=state.DeepClone();
        var result=fn(copy);
        state=copy;
        return Task.FromResult(result);
    }
}
sealed class FakeGeneration : IWorkGenerationGateway
{
    public List<string> Dispatches { get; } = new();
    public void Dispatch(WorkState work,string stage)=>Dispatches.Add(stage);
    public Task<bool> IsEnabledAsync(string stage)=>Task.FromResult(true);
}
