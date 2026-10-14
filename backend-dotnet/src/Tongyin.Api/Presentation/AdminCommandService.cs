using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tongyin.Api.Core;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Presentation;

public sealed class AdminCommandService(CompatAuth auth, IStore db, MediaService media)
{
    private static async Task<(IResult? Error,string Actor)> Authorize(HttpContext ctx,CompatAuth auth,params string[] roles)
    {
        var person=await auth.VerifyAdmin(ctx);
        if(person==null)return (Fail(401,"后台登录已失效"),"");
        if(person.Value.Role!="owner" && !roles.Contains(person.Value.Role))
            return (Fail(403,"当前角色无权进行此操作"),"");
        return (null,person.Value.Name);
    }

    // GET /api/admin/accounts
    public async Task<IResult> ListAccounts(HttpContext ctx)
    {
            if(await auth.ProtectAdmin(ctx,true) is {} denied)return denied;
            return Ok(await db.Read(s=>s.Admins.Values.Select(a=>new {
                username=Doc.Str(a,"owner"),role=Doc.Str(a,"role"),disabled=Doc.Bool(a,"disabled"),
                created_at=Doc.Str(a,"created_at")
            }).ToArray()));
    }

    // POST /api/admin/accounts
    public async Task<IResult> CreateAccount(HttpContext ctx)
    {
            if(await auth.ProtectAdmin(ctx,true) is {} denied)return denied;
            var body=await Body(ctx);if(body==null)return Fail(400,"参数错误");
            try
            {
                var username=Doc.Str(body,"username");
                var newAccount=CompatAuth.NewAdmin(username,Doc.Str(body,"password"),Doc.Str(body,"role"));
                await db.Update(s=>
                {
                    if(s.Admins.ContainsKey(username))throw new InvalidOperationException("账号已存在");
                    s.Admins[username]=newAccount;
                    Record(s,"system","admin.created",username,Doc.Str(body,"role"));
                    return true;
                });
                return Ok(new {username,role=Doc.Str(body,"role")},201);
            }
            catch(ArgumentException e){return Fail(400,e.Message);}
            catch(Exception e){return Fail(409,e.Message);}
    }

    // PUT /api/admin/accounts/{id}
    public async Task<IResult> UpdateAccount(string id, HttpContext ctx)
    {
            if(await auth.ProtectAdmin(ctx,true) is {} denied)return denied;
            var body=await Body(ctx);if(body==null)return Fail(400,"参数错误");
            var role=Doc.Str(body,"role");
            if(role!="" && role is not ("owner" or "operator" or "production" or "shipping" or "service"))
                return Fail(400,"角色不存在");
            var password=Doc.Str(body,"password");
            if(password!="" && password.Length<12)return Fail(400,"后台密码至少12位");
            try
            {
                await db.Update(s=>
                {
                    if(!s.Admins.TryGetValue(id,out var a))throw new KeyNotFoundException();
                    var disabled=body["disabled"] is null ? Doc.Bool(a,"disabled") : Doc.Bool(body,"disabled");
                    var newRole=role==""?Doc.Str(a,"role"):role;
                    if(Doc.Str(a,"role")=="owner" && (disabled || newRole!="owner") &&
                       s.Admins.Values.Count(v=>Doc.Str(v,"role")=="owner" && !Doc.Bool(v,"disabled"))<=1)
                        throw new InvalidOperationException("至少保留一个可用的超级管理员");
                    a["role"]=newRole;a["disabled"]=disabled;
                    if(password!="")
                    {
                        var newAdmin=CompatAuth.NewAdmin(id,password,newRole);
                        a["salt"]=Doc.Str(newAdmin,"salt");
                        a["password_hash"]=Doc.Str(newAdmin,"password_hash");
                    }
                    a["version"]=Doc.Int(a,"version")+1;
                    Record(s,"system","admin.updated",id);
                    return true;
                });
                return Ok(new {status="ok"});
            }
            catch(KeyNotFoundException){return Fail(404,"管理员不存在");}
            catch(Exception e){return Fail(409,e.Message);}
    }

    // POST /api/admin/works/{id}/asset
    public async Task<IResult> UploadWorkAsset(string id, HttpContext ctx)
    {
            var guard=await Authorize(ctx,auth,"operator","production");
            if(guard.Error!=null)return guard.Error;
            var kind=ctx.Request.Query["kind"].ToString();
            if(kind is not ("candidate" or "preview" or "model"))
                return Fail(400,"kind 必须是 candidate、preview 或 model");
            if(!await db.Read(s=>s.Works.ContainsKey(id)))return Fail(404,"作品不存在");
            string file;
            try
            {
                var form=await ctx.Request.ReadFormAsync();
                var source=form.Files.GetFile("file");
                if(source==null)return Fail(400,"请上传文件");
                (file,_)=await media.Save(source,kind!="model");
            }
            catch(ArgumentException e){return Fail(400,e.Message);}
            try
            {
                var updated=await db.Update(s=>
                {
                    if(!s.Works.TryGetValue(id,out var w))throw new KeyNotFoundException();
                    var status=Doc.Str(w,"status");
                    switch(kind)
                    {
                        case "candidate":
                            if(status!="image_pending" && status!="image_ready" &&
                                !(status=="failed" && Doc.Int(w,"selected",-1)<0))
                                throw new InvalidOperationException("当前阶段不可上传形象图");
                            w["candidates"]=new JsonArray(file);w["preview"]=new JsonArray();w["model"]="";
                            if(Doc.Int(w,"image_attempts")==0)w["image_attempts"]=1;
                            w["selected"]=-1;w["status"]="image_ready";w["note"]="";
                            break;
                        case "preview":
                            if(status is not ("model_pending" or "model_ready"))
                                throw new InvalidOperationException("请等待用户确认形象后上传预览图");
                            if(Doc.Array(w,"preview").Count>=4)throw new InvalidOperationException("最多4张预览图");
                            if(w["preview"] is not JsonArray)w["preview"]=new JsonArray();
                            ((JsonArray)w["preview"]!).Add(file);
                            if(Doc.Str(w,"model")!="")w["status"]="model_ready";
                            break;
                        case "model":
                            if(status is not ("model_pending" or "model_ready"))
                                throw new InvalidOperationException("请等待用户确认形象后上传模型");
                            w["model"]=file;w["preview"]=new JsonArray();w["status"]="model_ready";
                            break;
                    }
                    Record(s,guard.Actor,"work.asset",id,kind);
                    return ExposeWork(w,media);
                });
                return Ok(updated);
            }
            catch(Exception e){File.Delete(media.Absolute(file));return Fail(409,e.Message);}
    }

    // POST /api/admin/works/{id}/status
    public async Task<IResult> UpdateWorkStatus(string id, HttpContext ctx)
    {
            var guard=await Authorize(ctx,auth,"operator","production");
            if(guard.Error!=null)return guard.Error;
            var body=await Body(ctx);if(body==null)return Fail(400,"参数无效");
            var status=Doc.Str(body,"Status",Doc.Str(body,"status"));
            if(status is not ("failed" or "image_pending" or "model_pending" or "model_ready"))
                return Fail(400,"状态无效");
            try
            {
                await db.Update(s=>
                {
                    if(!s.Works.TryGetValue(id,out var w))throw new KeyNotFoundException();
                    if(status=="model_ready" && Doc.Str(w,"model")=="")
                        throw new InvalidOperationException("缺少 GLB 模型");
                    w["status"]=status;w["note"]=Doc.Str(body,"Note",Doc.Str(body,"note")).Trim();
                    Record(s,guard.Actor,"work.status",id,status);
                    return true;
                });
                return Ok(new{status="ok"});
            }
            catch(KeyNotFoundException){return Fail(404,"作品不存在");}
            catch(Exception e){return Fail(400,e.Message);}
    }

    // POST /api/admin/orders/{id}/status
    public async Task<IResult> UpdateOrderStatus(string id, HttpContext ctx)
    {
            var guard=await Authorize(ctx,auth,"operator","shipping");
            if(guard.Error!=null)return guard.Error;
            var body=await Body(ctx);if(body==null)return Fail(400,"参数无效");
            var next=Doc.Str(body,"Status",Doc.Str(body,"status"));
            if(next is not ("printing" or "shipped" or "completed" or "canceled"))return Fail(400,"状态无效");
            try
            {
                await db.Update(s=>
                {
                    if(!s.Orders.TryGetValue(id,out var o))throw new KeyNotFoundException();
                    var current=Doc.Str(o,"status");
                    if(current is "canceled" or "completed")throw new InvalidOperationException("订单已结束");
                    if(current=="awaiting_payment" && next!="canceled")throw new InvalidOperationException("订单尚未支付");
                    if(next=="canceled" && Doc.Str(o,"payment_started_at")!="")throw new InvalidOperationException("支付已发起，不能直接取消");
                    if(next=="canceled" && current!="awaiting_payment")throw new InvalidOperationException("已支付订单必须通过退款流程");
                    if(next=="printing" && Doc.Str(o,"refund_status")!="")throw new InvalidOperationException("有退款申请，不能生产");
                    var flow=new Dictionary<string,string>{{"paid","printing"},{"printing","shipped"},{"shipped","completed"}};
                    if(next!="canceled" && (!flow.TryGetValue(current,out var expected) || expected!=next))
                        throw new InvalidOperationException("不允许跳过订单状态");
                    var tracking=Doc.Str(body,"Tracking",Doc.Str(body,"tracking")).Trim();
                    if(next=="shipped" && tracking=="")throw new InvalidOperationException("请填写运单号");
                    o["status"]=next;
                    if(tracking!="")o["tracking"]=tracking;
                    if(o["history"] is not JsonArray)o["history"]=new JsonArray();
                    ((JsonArray)o["history"]!).Add(new JsonObject{["at"]=Doc.Now(),["actor"]="admin",["action"]="status."+next});
                    Record(s,guard.Actor,"order.status",id,next);
                    return true;
                });
                return Ok(new {status="ok"});
            }
            catch(KeyNotFoundException){return Fail(404,"订单不存在");}
            catch(Exception e){return Fail(409,e.Message);}
    }

    // POST /api/admin/orders/{id}/refund
    public async Task<IResult> RefundOrder(string id, HttpContext ctx)
    {
            var guard=await Authorize(ctx,auth,"operator","shipping");
            if(guard.Error!=null)return guard.Error;
            var body=await Body(ctx);
            var reason=Doc.Str(body,"reason").Trim();
            if(reason.EnumerateRunes().Count() is <3 or >200)return Fail(400,"请填写退款原因");
            try
            {
                await db.Update(s=>
                {
                    if(!s.Orders.TryGetValue(id,out var o))throw new KeyNotFoundException();
                    if(Doc.Str(o,"status") is not ("paid" or "printing"))
                        throw new InvalidOperationException("只有已付款或制作中的订单能申请退款");
                    if(Doc.Str(o,"refund_status")!="")throw new InvalidOperationException("该订单已有退款记录");
                    o["refund_status"]="requested";
                    if(o["history"] is not JsonArray)o["history"]=new JsonArray();
                    ((JsonArray)o["history"]!).Add(new JsonObject{["at"]=Doc.Now(),["actor"]=guard.Actor,["action"]="refund_requested",["note"]=reason});
                    Record(s,guard.Actor,"order.refund_requested",id,reason);
                    return true;
                });
                return Ok(new{refund_status="requested",message="已记录退款申请，请在微信商户平台实际退款并核对资金"});
            }
            catch(KeyNotFoundException){return Fail(404,"订单不存在");}
            catch(Exception e){return Fail(409,e.Message);}
    }

}
