using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Tongyin.Api.Core;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Api;

public static class AdminEndpoints
{
    public static void MapAdminFoundation(this WebApplication app)
    {
        app.MapPost("/api/admin/auth/login",async (HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            var body=await Body(ctx);
            var username=Doc.Str(body,"username");
            var password=Doc.Str(body,"password");
            var account=await db.Read(s=>s.Admins.TryGetValue(username,out var v)? Doc.Copy(v):null);
            if(account==null || Doc.Bool(account,"disabled") || password=="")return Fail(401,"账号或密码错误");
            var salt=Doc.Str(account,"salt");
            var stored=Doc.Str(account,"password_hash");
            var candidate=CompatAuth.PasswordHash(password,salt);
            if(stored.Length!=candidate.Length || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(stored),Encoding.ASCII.GetBytes(candidate)))return Fail(401,"账号或密码错误");
            return Ok(new {token=auth.AdminToken(account),username,role=Doc.Str(account,"role")});
        });
        app.MapGet("/api/admin/me",async (HttpContext ctx,CompatAuth auth)=>
        {
            var info=await auth.VerifyAdmin(ctx);
            return info==null ? Fail(401,"后台登录已失效"):Ok(new{username=info.Value.Name,role=info.Value.Role});
        });
        app.MapGet("/api/admin/dashboard",async (HttpContext ctx,CompatAuth auth,IStore db,Settings cfg)=>
        {
            if(await auth.ProtectAdmin(ctx) is { } denied)return denied;
            return Ok(await db.Read(s=>
            {
                var keys=new[]{"users","works","orders","image_pending","model_pending","awaiting_payment","paid","printing","shipped","completed","refund_requested"};
                var metrics=keys.ToDictionary(k=>k,_=>0);
                metrics["users"]=s.Users.Count;
                metrics["works"]=s.Works.Count;
                metrics["orders"]=s.Orders.Count;
                foreach(var w in s.Works.Values)
                {
                    var status=Doc.Str(w,"status");
                    if(metrics.ContainsKey(status))metrics[status]++;
                }
                var revenue=0;
                foreach(var o in s.Orders.Values)
                {
                    var status=Doc.Str(o,"status");
                    if(metrics.ContainsKey(status))metrics[status]++;
                    if(Doc.Str(o,"refund_status")=="requested")metrics["refund_requested"]++;
                    if(status!="awaiting_payment" && status!="canceled")revenue+=Doc.Int(o,"price_fen");
                }
                return new {metrics,paid_gross_fen=revenue,pay_configured=cfg.PayReady,
                    tracking_configured=cfg.Kuaidi100Key!="" && cfg.Kuaidi100Customer!=""};
            }));
        });
        app.MapGet("/api/admin/works",async (HttpContext ctx,CompatAuth auth,IStore db,MediaService media)=>
        {
            if(await auth.ProtectAdmin(ctx) is {} denied)return denied;
            return Ok(await db.Read(s=>s.Works.Values.Select(w=>ExposeAdminWork(s,w,media)).ToArray()));
        });
        app.MapGet("/api/admin/orders",async (HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            if(await auth.ProtectAdmin(ctx) is {} denied)return denied;
            return Ok(await db.Read(s=>s.Orders.Values.Select(o=>ExposeAdminOrder(s,o)).ToArray()));
        });
        app.MapGet("/api/admin/users",async (HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            if(await auth.ProtectAdmin(ctx) is {} denied)return denied;
            return Ok(await db.Read(s=>s.Users.Values.Select(u=>UserSummary(s,u))
                .OrderByDescending(u=>Doc.Str(u,"created_at")).ToArray()));
        });
        app.MapGet("/api/admin/audit",async (HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            if(await auth.ProtectAdmin(ctx,true) is {} denied)return denied;
            return Ok(await db.Read(s=>s.Audit.Values.OrderByDescending(a=>Doc.Str(a,"created_at"))
                .Take(300).Select(Doc.Copy).ToArray()));
        });
        app.MapGet("/api/admin/search/works",async (HttpContext ctx,CompatAuth auth,IStore db,MediaService media)=>
        {
            if(await auth.ProtectAdmin(ctx) is {} denied)return denied;
            return Ok(await db.Read(s =>
            {
                var rows=s.Works.Values.Where(w=>Matches(w,ctx));
                var view=rows.Select(w=>ExposeAdminWork(s,w,media));
                return Page(view,ctx);
            }));
        });
        app.MapGet("/api/admin/search/orders",async (HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            if(await auth.ProtectAdmin(ctx) is {} denied)return denied;
            return Ok(await db.Read(s =>
            {
                var rows=s.Orders.Values.Where(o=>Matches(o,ctx));
                if(ctx.Request.Query["category"]=="shipping")rows=rows.Where(o=>Doc.Str(o,"status") is "printing" or "shipped" or "completed");
                return Page(rows.Select(o=>ExposeAdminOrder(s,o)),ctx);
            }));
        });
        app.MapGet("/api/admin/search/users",async (HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            if(await auth.ProtectAdmin(ctx) is {} denied)return denied;
            return Ok(await db.Read(s=>Page(s.Users.Values.Select(u=>UserSummary(s,u))
                .Where(u=>Matches(u,ctx)),ctx)));
        });
        app.MapPost("/api/admin/users/{id}/status",async (string id,HttpContext ctx,CompatAuth auth,IStore db)=>
        {
            if(await auth.ProtectAdmin(ctx) is {} denied)return denied;
            var body=await Body(ctx);
            if(body is null)return Fail(400,"参数错误");
            var disabled=Doc.Bool(body,"disabled");
            var admin=(await auth.VerifyAdmin(ctx))!.Value;
            var found=await db.Update(s=>
            {
                var user=s.Users.Values.FirstOrDefault(u=>Doc.Str(u,"id")==id);
                if(user==null)return false;
                user["disabled"]=disabled;
                Record(s,admin.Name,"user.status",id,"disabled="+disabled);
                return true;
            });
            return found ? Ok(new {status="ok"}) : Fail(404,"用户不存在");
        });
    }

    public static JsonObject ExposeAdminWork(Snapshot s,JsonObject work,MediaService media)
    {
        var v=ExposeWork(work,media);
        var owner=Doc.Str(work,"owner");
        v["owner_user_id"]=s.Users.TryGetValue(owner,out var u)?Doc.Str(u,"id"):"";
        return v;
    }
    public static JsonObject ExposeAdminOrder(Snapshot s,JsonObject order)
    {
        var v=ExposeOrder(order);
        var owner=Doc.Str(order,"owner");
        v["owner_user_id"]=s.Users.TryGetValue(owner,out var u)?Doc.Str(u,"id"):"";
        return v;
    }
    private static JsonObject UserSummary(Snapshot s,JsonObject u)
    {
        var owner=Doc.Str(u,"owner");
        return new JsonObject {
            ["id"]=Doc.Str(u,"id"),["created_at"]=Doc.Str(u,"created_at"),
            ["last_login"]=Doc.Str(u,"last_login"),["disabled"]=Doc.Bool(u,"disabled"),
            ["works"]=s.Works.Values.Count(w=>Doc.Str(w,"owner")==owner),
            ["orders"]=s.Orders.Values.Count(o=>Doc.Str(o,"owner")==owner)
        };
    }
    private static bool Matches(JsonObject item,HttpContext ctx)
    {
        var filter=ctx.Request.Query["status"].ToString();
        if(filter!="" && Doc.Str(item,"status")!=filter)return false;
        var search=ctx.Request.Query["q"].ToString().Trim();
        if(search=="")return true;
        return item.ToJsonString().Contains(search,StringComparison.OrdinalIgnoreCase);
    }
    private static object Page(IEnumerable<JsonObject> docs,HttpContext ctx)
    {
        var page=int.TryParse(ctx.Request.Query["page"],out var p) ? Math.Clamp(p,1,100000) : 1;
        var size=int.TryParse(ctx.Request.Query["page_size"],out var n) ? Math.Clamp(n,1,100) : 12;
        var sorted=docs.OrderByDescending(d=>Doc.Str(d,"created_at")).ThenByDescending(d=>Doc.Str(d,"id")).ToArray();
        return new { items=sorted.Skip((page-1)*size).Take(size).ToArray(),total=sorted.Length,page,page_size=size };
    }
}
