using Tongyin.Api.Core;
using Tongyin.Api.Api;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Persistence;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Presentation;

// Thin HTTP adapter kept for the unchanged controller signatures.
// Authentication, multipart parsing and response projection belong to the API.
/// <summary>
/// 作品上传、查询及生成任务调用的 HTTP 适配服务。
/// </summary>
public sealed class WorkService(CompatAuth auth,MediaService media,Settings cfg,WorkUseCases works)
{
    public async Task<IResult> UploadDrawing(HttpContext ctx)
    {
        var owner=await auth.Customer(ctx);
        if(owner is null)return Fail(401,"请登录");
        try
        {
            var form=await ctx.Request.ReadFormAsync();
            var file=form.Files.GetFile("file");
            if(file is null)return Fail(400,"未找到上传文件");
            var stored=await media.Save(file,true);
            var created=await works.CreateAsync(owner,stored.Name);
            return Ok(ExposeWork(LegacyWorkDocument.ToDocument(created),media),201);
        }
        catch(ArgumentException e){return Fail(400,e.Message);}
        catch(Exception){return Fail(500,"保存作品失败");}
    }

    /// <summary>
    /// 查询当前用户拥有的作品。
    /// </summary>
    public async Task<IResult> ListWorks(HttpContext ctx)
    {
        var owner=await auth.Customer(ctx);
        if(owner is null)return Fail(401,"请登录");
        var found=await works.ListAsync(owner);
        return Ok(found.Select(w=>ExposeWork(LegacyWorkDocument.ToDocument(w),media)).ToArray());
    }

    /// <summary>
    /// 查找当前用户的指定作品。
    /// </summary>
    public async Task<IResult> GetWork(string id,HttpContext ctx)
    {
        var owner=await auth.Customer(ctx);
        if(owner is null)return Fail(401,"请登录");
        var found=await works.GetAsync(owner,id);
        return found is null ? Fail(404,"作品不存在") :
            Ok(ExposeWork(LegacyWorkDocument.ToDocument(found),media));
    }

    /// <summary>
    /// 提交卡通形象生成请求。
    /// </summary>
    public async Task<IResult> GenerateImage(string id,HttpContext ctx)
    {
        var owner=await auth.Customer(ctx);
        if(owner is null)return Fail(401,"请登录");
        try
        {
            var receipt=await works.GenerateAsync(owner,id,cfg.FreeImageAttempts);
            return Ok(new {
                status="image_pending", image_attempts=receipt.Work.ImageAttempts,
                free_image_attempts=cfg.FreeImageAttempts,
                message=receipt.Automatic?"AI 生成任务已提交":"已进入人工处理队列"
            },202);
        }
        catch(KeyNotFoundException){return Fail(404,"作品不存在");}
        catch(Exception e){return Fail(409,e.Message);}
    }

    /// <summary>
    /// 确认候选形象并提交模型生成请求。
    /// </summary>
    public async Task<IResult> SelectImage(string id,HttpContext ctx)
    {
        var owner=await auth.Customer(ctx);
        if(owner is null)return Fail(401,"请登录");
        var body=await Body(ctx);
        if(body is null)return Fail(400,"参数有误");
        try
        {
            var receipt=await works.SelectAsync(owner,id,Doc.Int(body,"index",-1));
            return Ok(new {
                status="model_pending",
                message=receipt.Automatic?"AI 生成任务已提交":"已进入人工处理队列"
            },202);
        }
        catch(KeyNotFoundException){return Fail(404,"作品不存在");}
        catch(Exception e){return Fail(400,e.Message);}
    }
}
