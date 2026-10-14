using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Application;

public sealed class CustomerService(Settings cfg, IStore store, CompatAuth auth, IHttpClientFactory factory, MediaService media)
{

    // POST /api/auth/wechat
    public async Task<IResult> LoginWechat(HttpContext ctx)
    {
            var body=await Body(ctx);
            var code=Doc.Str(body,"code");
            if (code == "") return Fail(400,"参数不合法");
            string owner;
            if (code == "DEV_LOGIN" && cfg.DevMode) owner="dev-user";
            else
            {
                if (cfg.WxAppId=="" || cfg.WxAppSecret=="") return Fail(503,"尚未配置微信 AppID/AppSecret");
                var endpoint="https://api.weixin.qq.com/sns/jscode2session?appid="+Uri.EscapeDataString(cfg.WxAppId)+
                             "&secret="+Uri.EscapeDataString(cfg.WxAppSecret)+"&js_code="+Uri.EscapeDataString(code)+
                             "&grant_type=authorization_code";
                try
                {
                    using var response=await factory.CreateClient("wechat").GetAsync(endpoint);
                    var raw=Doc.Parse(await response.Content.ReadAsStringAsync());
                    owner=Doc.Str(raw,"openid");
                    if (!response.IsSuccessStatusCode || owner=="" || Doc.Int(raw,"errcode") != 0) return Fail(401,"微信登录失败，请重试");
                }
                catch {return Fail(502,"微信登录请求失败");}
            }
            try
            {
                var disabled=await store.Update(s =>
                {
                    if (!s.Users.TryGetValue(owner,out var user))
                    {
                        s.Users[owner]=new JsonObject { ["id"]=Doc.NewId(),["owner"]=owner,["created_at"]=Doc.Now(),["last_login"]=Doc.Now(),["disabled"]=false };
                        return false;
                    }
                    if (Doc.Bool(user,"disabled")) return true;
                    user["last_login"]=Doc.Now();
                    return false;
                });
                return disabled ? Fail(403,"账号已停用") : Ok(new {token=auth.CustomerToken(owner)});
            }
            catch {return Fail(500,"登录记录失败");}
    }

    // GET /api/me
    public async Task<IResult> GetProfile(HttpContext ctx)
    {
            var owner=await auth.Customer(ctx);
            if (owner == null) return Fail(401,"请登录");
            var result=await store.Read(s =>
            {
                var u=s.Users[owner];
                return new { id=Doc.Str(u,"id"),created_at=Doc.Str(u,"created_at"),works=s.Works.Values.Count(w=>Doc.Str(w,"owner")==owner),
                    orders=s.Orders.Values.Count(o=>Doc.Str(o,"owner")==owner) };
            });
            return Ok(result);
    }

    public Task<IResult> ServeMedia(string file, HttpContext ctx) =>
        media.Serve(file, ctx.Request.Query["expires"], ctx.Request.Query["signature"]);

}
