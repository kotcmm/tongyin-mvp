using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Api;

// Migration phase 1: original art upload, signed media and ownership-safe reads.
// Generation will be wired to a dedicated AI service in the next phase.
public static class WorkEndpoints
{
    public static void MapWorks(this WebApplication app)
    {
        app.MapPost("/api/works", async (HttpContext ctx,CompatAuth auth,IStore store,MediaService media) =>
        {
            var owner=await auth.Customer(ctx);
            if (owner is null) return Fail(401,"请登录");
            try
            {
                var form=await ctx.Request.ReadFormAsync();
                var file=form.Files.GetFile("file");
                if (file is null) return Fail(400,"未找到上传文件");
                var stored=await media.Save(file,true);
                var work=new JsonObject{
                    ["id"]=Doc.NewId(),["owner"]=owner,["created_at"]=Doc.Now(),
                    ["original"]=stored.Name,["candidates"]=new JsonArray(),["selected"]=-1,
                    ["preview"]=new JsonArray(),["model"]="",["status"]="image_pending",["image_attempts"]=0
                };
                await store.Update(s=>{ s.Works[Doc.Str(work,"id")]=work;return true;});
                return Ok(ExposeWork(work,media),201);
            }
            catch(ArgumentException e){return Fail(400,e.Message);}
            catch(Exception){return Fail(500,"保存作品失败");}
        });
        app.MapGet("/api/works", async (HttpContext ctx,CompatAuth auth,IStore store,MediaService media) =>
        {
            var owner=await auth.Customer(ctx);
            if (owner is null) return Fail(401,"请登录");
            var works=await store.Read(s=>s.Works.Values.Where(w=>Doc.Str(w,"owner")==owner)
                .OrderByDescending(w=>Doc.Str(w,"created_at")).Select(w=>ExposeWork(w,media)).ToArray());
            return Ok(works);
        });
        app.MapGet("/api/works/{id}", async (string id,HttpContext ctx,CompatAuth auth,IStore store,MediaService media) =>
        {
            var owner=await auth.Customer(ctx);
            if (owner is null) return Fail(401,"请登录");
            var work=await store.Read(s => s.Works.TryGetValue(id,out var w) && Doc.Str(w,"owner")==owner ? ExposeWork(w,media) : null);
            return work == null ? Fail(404,"作品不存在") : Ok(work);
        });
    }
}
