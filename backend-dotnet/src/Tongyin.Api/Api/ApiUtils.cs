using System.Text.Json.Nodes;
using Tongyin.Api.Core;

namespace Tongyin.Api.Api;

public static class ApiUtils
{
    public static IResult Fail(int code,string message) => Results.Json(new { error = message }, statusCode: code);
    public static IResult Ok(JsonObject doc,int status=200) => Results.Json(doc,statusCode:status);
    public static IResult Ok(object doc,int status=200) => Results.Json(doc,statusCode:status);
    public static async Task<JsonObject?> Body(HttpContext ctx)
    {
        if (ctx.Request.ContentLength > 1 << 20) return null;
        try
        {
            ctx.Request.EnableBuffering();
            return await ctx.Request.ReadFromJsonAsync<JsonObject>();
        }
        catch { return null; }
    }
    public static JsonObject ExposeWork(JsonObject work,MediaService media)
    {
        var v=Doc.Copy(work);
        v["owner"] = "";
        foreach (var key in new []{"original","model"})
            v[key] = media.Signed(Doc.Str(work,key));
        foreach(var key in new []{"candidates","preview"})
        {
            var arr=new JsonArray();
            foreach (var item in Doc.Array(work,key)) arr.Add(media.Signed(item?.ToString()));
            v[key] = arr;
        }
        return v;
    }
    public static JsonObject ExposeOrder(JsonObject order)
    {
        var v=Doc.Copy(order);
        v["owner"] = "";
        return v;
    }
    public static JsonObject Audit(string who,string action,string id,string detail="")
        => new() { ["id"]=Doc.NewId(), ["owner"]=who,["created_at"]=Doc.Now(), ["action"]=action,["target"]=id,["detail"]=detail };
    public static void Record(Snapshot snap,string who,string action,string id,string detail="")
    {
        var audit=Audit(who,action,id,detail);
        snap.Audit[Doc.Str(audit,"id")]=audit;
    }
    public static IResult Error(Exception e) => e is KeyNotFoundException
        ? Fail(404,"记录不存在")
        : e is ArgumentException ? Fail(400,e.Message) : Fail(409,e.Message);
}
