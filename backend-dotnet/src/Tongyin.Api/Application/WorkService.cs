using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Application;

// Migration phase 1: original art upload, signed media and ownership-safe reads.
// Generation will be wired to a dedicated AI service in the next phase.
public sealed class WorkService(CompatAuth auth, IStore store, MediaService media, AiService ai, Settings cfg)
{

    // POST /api/works
    public async Task<IResult> UploadDrawing(HttpContext ctx)
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
    }

    // GET /api/works
    public async Task<IResult> ListWorks(HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);
            if (owner is null) return Fail(401,"请登录");
            var works=await store.Read(s=>s.Works.Values.Where(w=>Doc.Str(w,"owner")==owner)
                .OrderByDescending(w=>Doc.Str(w,"created_at")).Select(w=>ExposeWork(w,media)).ToArray());
            return Ok(works);
    }

    // GET /api/works/{id}
    public async Task<IResult> GetWork(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);
            if (owner is null) return Fail(401,"请登录");
            var work=await store.Read(s => s.Works.TryGetValue(id,out var w) && Doc.Str(w,"owner")==owner ? ExposeWork(w,media) : null);
            return work == null ? Fail(404,"作品不存在") : Ok(work);
    }

    // POST /api/works/{id}/generate
    public async Task<IResult> GenerateImage(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            try
            {
                var work=await store.Update(s=>
                {
                    if(!s.Works.TryGetValue(id,out var w) || Doc.Str(w,"owner")!=owner)throw new KeyNotFoundException();
                    var status=Doc.Str(w,"status");
                    if(status!="image_ready" && status!="image_pending" &&
                       !(status=="failed" && Doc.Int(w,"selected",-1)<0))
                        throw new InvalidOperationException("当前作品不能重新生成形象");
                    if(status=="image_pending" && Doc.Int(w,"image_attempts")>0)
                        throw new InvalidOperationException("形象正在生成，请勿重复提交");
                    if(Doc.Int(w,"image_attempts")>=cfg.FreeImageAttempts)
                        throw new InvalidOperationException("免费生成次数已用完");
                    w["image_attempts"]=Doc.Int(w,"image_attempts")+1;
                    w["generation_id"]=Doc.NewId();
                    w["status"]="image_pending";w["selected"]=-1;
                    w["candidates"]=new JsonArray();w["preview"]=new JsonArray();
                    w["model"]="";w["note"]="";
                    return Doc.Copy(w);
                });
                ai.Dispatch(work,"image");
                var msg=await ai.Enabled("image") ? "AI 生成任务已提交" : "已进入人工处理队列";
                return Ok(new {status="image_pending",image_attempts=Doc.Int(work,"image_attempts"),
                    free_image_attempts=cfg.FreeImageAttempts,message=msg},202);
            }
            catch(KeyNotFoundException){return Fail(404,"作品不存在");}
            catch(Exception e){return Fail(409,e.Message);}
    }

    // POST /api/works/{id}/select
    public async Task<IResult> SelectImage(string id, HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var body=await Body(ctx);
            if(body==null)return Fail(400,"参数有误");
            try
            {
                var work=await store.Update(s=>
                {
                    if(!s.Works.TryGetValue(id,out var w) || Doc.Str(w,"owner")!=owner)throw new KeyNotFoundException();
                    if(Doc.Str(w,"status")!="image_ready" || Doc.Int(body,"index",-1)!=0 ||
                       Doc.Array(w,"candidates").Count<1)throw new ArgumentException("无法选择此图片");
                    var first=Doc.Array(w,"candidates")[0]?.ToString()??"";
                    w["candidates"]=new JsonArray(first);
                    w["selected"]=0;
                    w["preview"]=new JsonArray();
                    w["model"]="";
                    w["status"]="model_pending";
                    return Doc.Copy(w);
                });
                ai.Dispatch(work,"model");
                return Ok(new {status="model_pending",message=await ai.Enabled("model")?"AI 生成任务已提交":"已进入人工处理队列"},202);
            }
            catch(KeyNotFoundException){return Fail(404,"作品不存在");}
            catch(Exception e){return Fail(400,e.Message);}
    }

}
