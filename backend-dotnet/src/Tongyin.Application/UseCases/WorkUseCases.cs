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

    public Task<JsonObject> QueueImage(string owner,string id,int freeAttempts) => store.Update(s=>
    {
        if(!s.Works.TryGetValue(id,out var w) || Doc.Str(w,"owner")!=owner)throw new KeyNotFoundException();
        var status=Doc.Str(w,"status");
        if(status!="image_ready" && status!="image_pending" &&
           !(status=="failed" && Doc.Int(w,"selected",-1)<0))
            throw new InvalidOperationException("当前作品不能重新生成形象");
        if(status=="image_pending" && Doc.Int(w,"image_attempts")>0)
            throw new InvalidOperationException("形象正在生成，请勿重复提交");
        if(Doc.Int(w,"image_attempts")>=freeAttempts)
            throw new InvalidOperationException("免费生成次数已用完");
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
        if(Doc.Str(w,"status")!="image_ready" || index!=0 ||
           Doc.Array(w,"candidates").Count<1)throw new ArgumentException("无法选择此图片");
        var first=Doc.Array(w,"candidates")[0]?.ToString()??"";
        w["candidates"]=new JsonArray(first);
        w["selected"]=0;
        w["preview"]=new JsonArray();
        w["model"]="";
        w["status"]="model_pending";
        return Doc.Copy(w);
    });
}
