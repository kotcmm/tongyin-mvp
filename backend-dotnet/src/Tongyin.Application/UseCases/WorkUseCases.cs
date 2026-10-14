using System.Text.Json.Nodes;
using Tongyin.Api.Core;

namespace Tongyin.Application.UseCases;

// Input is authenticated owner/id and domain data, NOT HTTP request objects.
// IStore is an inward-facing port and can be backed by File/PostgreSQL/MySQL.
public sealed class WorkUseCases(IStore store)
{
    public async Task<JsonObject> Create(string owner, string fileName)
    {
        var work=new JsonObject{
            ["id"]=Doc.NewId(),["owner"]=owner,["created_at"]=Doc.Now(),
            ["original"]=fileName,["candidates"]=new JsonArray(),["selected"]=-1,
            ["preview"]=new JsonArray(),["model"]="",["status"]="image_pending",["image_attempts"]=0
        };
        await store.Update(s=>{s.Works[Doc.Str(work,"id")]=work;return true;});
        return work;
    }

    public Task<JsonObject[]> List(string owner) => store.Read(s=>
        s.Works.Values.Where(w=>Doc.Str(w,"owner")==owner)
            .OrderByDescending(w=>Doc.Str(w,"created_at"))
            .Select(Doc.Copy).ToArray());

    public Task<JsonObject?> Find(string owner,string id) => store.Read(s=>
        s.Works.TryGetValue(id,out var w) && Doc.Str(w,"owner")==owner
            ? Doc.Copy(w) : null);

    public Task<JsonObject> QueueImage(string owner,string id,int freeAttempts) => store.Update(s=>
    {
        if(!s.Works.TryGetValue(id,out var w) || Doc.Str(w,"owner")!=owner)throw new KeyNotFoundException();
        WorkRules.RequireImageGeneration(w,freeAttempts);
        w["image_attempts"]=Doc.Int(w,"image_attempts")+1;
        w["generation_id"]=Doc.NewId();
        w["status"]="image_pending";w["selected"]=-1;
        w["candidates"]=new JsonArray();w["preview"]=new JsonArray();
        w["model"]="";w["note"]="";
        return Doc.Copy(w);
    });

    public Task<JsonObject> SelectImage(string owner,string id,int index) => store.Update(s=>
    {
        if(!s.Works.TryGetValue(id,out var w) || Doc.Str(w,"owner")!=owner)throw new KeyNotFoundException();
        WorkRules.RequireImageSelection(w,index);
        var first=Doc.Array(w,"candidates")[0]?.ToString()??"";
        w["candidates"]=new JsonArray(first);
        w["selected"]=0;
        w["preview"]=new JsonArray();
        w["model"]="";
        w["status"]="model_pending";
        return Doc.Copy(w);
    });
}
