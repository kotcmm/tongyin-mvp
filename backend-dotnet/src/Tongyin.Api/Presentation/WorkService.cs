using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Application.UseCases;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Presentation;

// Migration phase 1: original art upload, signed media and ownership-safe reads.
// Generation will be wired to a dedicated AI service in the next phase.
public sealed class WorkService(CompatAuth auth, IStore store, MediaService media, AiService ai, Settings cfg, WorkUseCases useCases)
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
                var work=await useCases.Create(owner,stored.Name);
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
                var work=await useCases.QueueImage(owner,id,cfg.FreeImageAttempts);
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
                var work=await useCases.SelectImage(owner,id,Doc.Int(body,"index",-1));
                ai.Dispatch(work,"model");
                return Ok(new {status="model_pending",message=await ai.Enabled("model")?"AI 生成任务已提交":"已进入人工处理队列"},202);
            }
            catch(KeyNotFoundException){return Fail(404,"作品不存在");}
            catch(Exception e){return Fail(400,e.Message);}
    }

}
