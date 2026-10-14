using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Api;

public static class AiEndpoints
{
    private static object View(JsonObject p) => new {
        id=Doc.Str(p,"id"), name=Doc.Str(p,"name"),stage=Doc.Str(p,"stage"),adapter=Doc.Str(p,"adapter"),
        endpoint=Doc.Str(p,"endpoint"),model=Doc.Str(p,"model"),enabled=Doc.Bool(p,"enabled"),
        @default=Doc.Bool(p,"default"),has_key=Doc.Str(p,"api_key_cipher")!="",
        created_at=Doc.Str(p,"created_at")
    };
    private static string? Validate(JsonObject q, bool dev)
    {
        var stage=Doc.Str(q,"stage");var adapter=Doc.Str(q,"adapter");
        var endpoint=Doc.Str(q,"endpoint");var model=Doc.Str(q,"model");
        if(Doc.Str(q,"name").Length is <1 or >60)return "模型名称无效";
        if(stage!="image" && stage!="model")return "能力类型无效";
        if(adapter!="seedream" && adapter!="worker")return "适配器类型无效";
        if(adapter=="seedream" && stage!="image")return "Seedream 不支持3D";
        if(adapter=="seedream" && model=="")return "请填写 Seedream 模型 ID";
        if(endpoint=="" && adapter=="seedream")endpoint="https://ark.cn-beijing.volces.com/api/v3/images/generations";
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri) ||
            (uri.Scheme!="https" && !(dev && adapter=="worker" && uri.Scheme=="http")) ||
            uri.UserInfo!="" || uri.Query!="" || uri.Fragment!="")return "API 地址必须是 HTTPS";
        if(adapter=="seedream" && endpoint!="https://ark.cn-beijing.volces.com/api/v3/images/generations")
            return "Seedream 必须使用官方方舟 API 地址";
        if(Doc.Bool(q,"default") && !Doc.Bool(q,"enabled"))return "默认模型必须启用";
        return null;
    }
    public static void MapAi(this WebApplication app)
    {
        app.MapGet("/api/admin/ai/config",async (HttpContext ctx,CompatAuth auth,IStore db,Settings cfg)=>
        {
            if(await auth.ProtectAdmin(ctx,true) is {} denied)return denied;
            return Ok(await db.Read(s=>new {
                providers=s.AIProviders.Values.Select(View).ToArray(),
                prompts=new { image=AiService.PromptOf(s,"image"), model=AiService.PromptOf(s,"model") },
                legacy_worker_enabled=cfg.AiWorkerUrl!=""
            }));
        });
        app.MapPost("/api/admin/ai/providers",async (HttpContext ctx,CompatAuth auth,IStore db,Settings cfg,AiService ai)=>
        {
            if(await auth.ProtectAdmin(ctx,true) is {} denied)return denied;
            var body=await Body(ctx);
            if(body==null)return Fail(400,"参数错误");
            var problem=Validate(body,cfg.DevMode);
            if(problem!=null)return Fail(400,problem);
            var encrypted="";
            var rawKey=Doc.Str(body,"api_key");
            if(rawKey.Length>4096)return Fail(400,"API Key 过长");
            if(rawKey!="")encrypted=ai.EncryptKey(rawKey.Trim());
            var stage=Doc.Str(body,"stage");
            var id=Doc.Str(body,"id");
            try
            {
                var saved=await db.Update(s=>
                {
                    JsonObject p;
                    if(id!="")
                    {
                        if(!s.AIProviders.TryGetValue(id,out var old))throw new KeyNotFoundException("提供商不存在");
                        if(Doc.Str(old,"stage")!=stage)throw new InvalidOperationException("能力类型不可修改，请新建提供商");
                        p=Doc.Copy(old);
                    }
                    else
                    {
                        id=Doc.NewId();
                        p=new JsonObject{["id"]=id,["owner"]="system",["created_at"]=Doc.Now()};
                    }
                    p["name"]=Doc.Str(body,"name").Trim();
                    p["stage"]=stage;p["adapter"]=Doc.Str(body,"adapter");
                    p["endpoint"]=Doc.Str(body,"endpoint")=="" && Doc.Str(body,"adapter")=="seedream"
                        ?"https://ark.cn-beijing.volces.com/api/v3/images/generations":Doc.Str(body,"endpoint");
                    p["model"]=Doc.Str(body,"model");p["enabled"]=Doc.Bool(body,"enabled");p["default"]=Doc.Bool(body,"default");
                    if(encrypted!="")p["api_key_cipher"]=encrypted;
                    if(Doc.Bool(p,"enabled") && Doc.Str(p,"api_key_cipher")=="")
                        throw new InvalidOperationException("启用前请设置 API Key");
                    if(Doc.Bool(p,"default"))foreach(var other in s.AIProviders.Values)
                        if(Doc.Str(other,"stage")==stage)other["default"]=false;
                    s.AIProviders[id]=p;
                    Record(s,"owner","ai.provider.saved",id,Doc.Str(p,"name"));
                    return View(p);
                });
                return Ok(saved);
            }
            catch(KeyNotFoundException){return Fail(404,"提供商不存在");}
            catch(Exception e){return Fail(409,e.Message);}
        });
        app.MapDelete("/api/admin/ai/providers/{id}",async (string id,HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            if(await auth.ProtectAdmin(ctx,true) is {} denied)return denied;
            var exists=await db.Update(s=>
            {
                if(!s.AIProviders.Remove(id))return false;
                Record(s,"owner","ai.provider.deleted",id);
                return true;
            });
            return exists?Ok(new {status="ok"}):Fail(404,"提供商不存在");
        });
        app.MapPut("/api/admin/ai/prompts/{stage}",async (string stage,HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            if(await auth.ProtectAdmin(ctx,true) is {} denied)return denied;
            if(stage!="image" && stage!="model")return Fail(400,"stage 无效");
            var body=await Body(ctx);
            var text=Doc.Str(body,"template").Trim();
            if(text.Length<10 || System.Text.Encoding.UTF8.GetByteCount(text)>12000)return Fail(400,"提示词应在10至12000字节之间");
            var prompt=await db.Update(s=>
            {
                var item=AiService.PromptOf(s,stage);
                item["version"]=Doc.Int(item,"version")+1;
                item["owner"]="system";
                item["created_at"]=Doc.Str(item,"created_at",Doc.Now());
                item["template"]=text;
                s.AIPrompts[stage]=item;
                Record(s,"owner","ai.prompt.saved",stage,"v"+Doc.Int(item,"version"));
                return Doc.Copy(item);
            });
            return Ok(prompt);
        });
        app.MapPost("/api/internal/ai-result",async (HttpContext ctx,IStore db,Settings cfg,AiService ai)=>
        {
            if(cfg.AiWebhookToken=="" || ctx.Request.Headers["X-AI-Token"]!=cfg.AiWebhookToken)
                return Fail(401,"无权限");
            var q=await Body(ctx);if(q==null)return Fail(400,"参数错误");
            var wid=Doc.Str(q,"work_id");var stage=Doc.Str(q,"stage");
            var generation=Doc.Str(q,"generation_id");
            if(stage!="image" && stage!="model")return Fail(400,"stage 无效");
            var current=await db.Read(s=>s.Works.TryGetValue(wid,out var w)?Doc.Copy(w):null);
            if(current==null)return Fail(404,"作品不存在");
            // Go accepts generation-less legacy works. New jobs must match exactly.
            static bool CurrentJob(JsonObject work,string phase,string gen) =>
                Doc.Str(work,"status")==phase &&
                (Doc.Str(work,"generation_id")=="" || Doc.Str(work,"generation_id")==gen);
            var expected=stage=="image"?"image_pending":"model_pending";
            if(!CurrentJob(current,expected,generation))return Fail(409,"旧生成任务已失效或状态已变化");
            var error=Doc.Str(q,"error");
            if(error!="")
            {
                try
                {
                    await db.Update(s=>
                    {
                        if(!s.Works.TryGetValue(wid,out var w) || !CurrentJob(w,expected,generation))
                            throw new InvalidOperationException("任务状态已变化");
                        w["status"]="failed";
                        w["note"]=error[..Math.Min(error.Length,200)];
                        return true;
                    });
                    return Ok(new{status="failed"});
                }
                catch(InvalidOperationException){return Fail(409,"任务状态已变化");}
            }
            var urls=Doc.Array(q,"urls");
            if(stage=="image" && (urls.Count!=1 || urls[0] is null))return Fail(400,"图片生成必须返回一张图片");
            if(stage=="model" && (Doc.Str(q,"model_url")=="" || urls.Count>4))return Fail(400,"模型阶段必须提供 GLB");
            var images=new List<string>();
            var downloaded=new List<string>();
            try
            {
                foreach(var url in urls)
                {
                    var file=await ai.Download(url?.ToString()??"",true);
                    images.Add(file);downloaded.Add(file);
                }
                var model="";
                if(stage=="model")
                {
                    model=await ai.Download(Doc.Str(q,"model_url"),false);
                    downloaded.Add(model);
                }
                await db.Update(s=>
                {
                    if(!s.Works.TryGetValue(wid,out var w) || !CurrentJob(w,expected,generation))
                        throw new InvalidOperationException("任务状态已变化");
                    if(stage=="image")
                    {
                        w["candidates"]=new JsonArray(images.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
                        w["status"]="image_ready";
                    }
                    else
                    {
                        w["model"]=model;
                        w["preview"]=new JsonArray(images.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
                        w["status"]="model_ready";
                    }
                    w["note"]="";
                    return true;
                });
                return Ok(new{status="ok"});
            }
            catch(InvalidOperationException)
            {
                foreach(var file in downloaded)File.Delete(ai.ResultPath(file));
                return Fail(409,"任务已变化或 AI 结果不合法");
            }
            catch(Exception)
            {
                foreach(var file in downloaded)File.Delete(ai.ResultPath(file));
                return Fail(502,"下载或写入模型失败");
            }
        });
    }
}
