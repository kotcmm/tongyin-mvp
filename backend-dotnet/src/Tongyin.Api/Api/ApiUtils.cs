using System.Text.Json.Nodes;
using Tongyin.Api.Core;

namespace Tongyin.Api.Api;

/// <summary>
/// 公共 HTTP（超文本传输协议）响应与旧版数据转换辅助类。
/// </summary>
public static class ApiUtils
{
    /// <summary>
    /// 构造带有状态码的 JSON 错误响应。
    /// </summary>
    public static IResult Fail(int code,string message) => Results.Json(new { error = message }, statusCode: code);
    /// <summary>
    /// 构造符合既有协议的 JSON 响应。
    /// </summary>
    public static IResult Ok(JsonObject doc,int status=200) => Results.Json(doc,statusCode:status);
    /// <summary>
    /// 构造符合既有协议的 JSON 响应。
    /// </summary>
    public static IResult Ok(object doc,int status=200) => Results.Json(doc,statusCode:status);
    /// <summary>
    /// 从 HTTP 请求体解析 JSON 对象。
    /// </summary>
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
    /// <summary>
    /// 过滤隐私字段并生成签名媒体 URL。
    /// </summary>
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
    /// <summary>
    /// 将订单转换为外部安全视图。
    /// </summary>
    public static JsonObject ExposeOrder(JsonObject order)
    {
        var v=Doc.Copy(order);
        v["owner"] = "";
        return v;
    }
    /// <summary>
    /// 生成一条后台操作审计记录。
    /// </summary>
    public static JsonObject Audit(string who,string action,string id,string detail="")
        => new() { ["id"]=Doc.NewId(), ["owner"]=who,["created_at"]=Doc.Now(), ["action"]=action,["target"]=id,["detail"]=detail };
    /// <summary>
    /// 向存储快照写入审计事件。
    /// </summary>
    public static void Record(Snapshot snap,string who,string action,string id,string detail="")
    {
        var audit=Audit(who,action,id,detail);
        snap.Audit[Doc.Str(audit,"id")]=audit;
    }
    /// <summary>
    /// 将业务异常映射为 HTTP 错误响应。
    /// </summary>
    public static IResult Error(Exception e) => e is KeyNotFoundException
        ? Fail(404,"记录不存在")
        : e is ArgumentException ? Fail(400,e.Message) : Fail(409,e.Message);
}
