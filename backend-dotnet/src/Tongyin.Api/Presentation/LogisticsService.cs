using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tongyin.Api.Core;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Presentation;

// Port of Go's real tracking-only logistics module. Never fabricates shipment events.
/// <summary>
/// 订单发货和快递轨迹的过渡 HTTP 适配服务。
/// </summary>
public sealed class LogisticsService(CompatAuth auth, IStore db, Settings cfg, IHttpClientFactory http)
{
    private static readonly Regex _carrierCode=new("^[a-z0-9]{2,30}$",RegexOptions.Compiled);
    private static readonly Regex _number=new("^[A-Za-z0-9_-]{5,40}$",RegexOptions.Compiled);
    private static async Task<(IResult? Error,string Actor)> Authorized(HttpContext ctx,CompatAuth auth)
    {
        var user=await auth.VerifyAdmin(ctx);
        if(user==null)return (Fail(401,"后台登录已失效"),"");
        if(user.Value.Role is not ("owner" or "operator" or "shipping"))
            return (Fail(403,"当前角色无权发货"),"");
        return (null,user.Value.Name);
    }

    private static void History(JsonObject order,string actor,string action,string note)
    {
        if(order["history"] is not JsonArray)order["history"]=new JsonArray();
        ((JsonArray)order["history"]!).Add(new JsonObject{["at"]=Doc.Now(),["actor"]=actor,["action"]=action,["note"]=note});
    }
    // POST /api/admin/orders/{id}/shipment
    /// <summary>
    /// 为订单登记发货信息。
    /// </summary>
    public async Task<IResult> CreateShipment(string id, HttpContext ctx)
    {
            var guard=await Authorized(ctx,auth);if(guard.Error!=null)return guard.Error;
            var req=await Body(ctx);if(req==null)return Fail(400,"参数错误");
            var carrier=Doc.Str(req,"carrier").Trim();
            var code=Doc.Str(req,"carrier_code").Trim().ToLowerInvariant();
            var tracking=Doc.Str(req,"tracking").Trim();
            if(carrier.EnumerateRunes().Count() is <2 or >40 || !_carrierCode.IsMatch(code) || !_number.IsMatch(tracking))
                return Fail(400,"请填写快递公司、快递100编码和有效运单号");
            try
            {
                await db.Update(s=>
                {
                    if(!s.Orders.TryGetValue(id,out var o))throw new KeyNotFoundException();
                    if(Doc.Str(o,"status")!="printing")throw new InvalidOperationException("必须在制作中状态完成打包后才能发货");
                    o["carrier"]=carrier;o["carrier_code"]=code;o["tracking"]=tracking;
                    o["status"]="shipped";o["shipped_at"]=Doc.Now();o["tracking_state"]="unavailable";
                    o["tracking_events"]=new JsonArray();o["tracking_checked_at"]="";
                    History(o,guard.Actor,"shipped",carrier+" "+tracking);
                    Record(s,guard.Actor,"order.shipped",id,carrier);
                    return true;
                });
                return Ok(new {status="shipped"});
            }
            catch(KeyNotFoundException){return Fail(404,"订单不存在");}
            catch(Exception e){return Fail(409,e.Message);}
    }

    // POST /api/admin/orders/{id}/tracking
    /// <summary>
    /// 写入人工物流轨迹信息。
    /// </summary>
    public async Task<IResult> AddTracking(string id, HttpContext ctx)
    {
            var guard=await Authorized(ctx,auth);if(guard.Error!=null)return guard.Error;
            var body=await Body(ctx);if(body==null)return Fail(400,"参数错误");
            var at=Doc.Str(body,"time").Trim();var content=Doc.Str(body,"context").Trim();
            if(content.EnumerateRunes().Count() is <3 or >300 || at.Length is <10 or >40)
                return Fail(400,"请填写物流时间和真实物流内容");
            try
            {
                await db.Update(s=>
                {
                    if(!s.Orders.TryGetValue(id,out var o))throw new KeyNotFoundException();
                    if(Doc.Str(o,"status") is not ("shipped" or "completed"))throw new InvalidOperationException("订单尚未发货");
                    if(o["tracking_events"] is not JsonArray)o["tracking_events"]=new JsonArray();
                    var events=(JsonArray)o["tracking_events"]!;
                    if(!events.Any(e=>Doc.Str(e,"time")==at && Doc.Str(e,"context")==content))
                        events.Add(new JsonObject{["time"]=at,["context"]=content,["source"]="manual"});
                    var copy=events.OrderByDescending(e=>Doc.Str(e,"time")).Select(e=>e?.DeepClone()).ToArray();
                    o["tracking_events"]=new JsonArray(copy);
                    o["tracking_checked_at"]=Doc.Now();o["tracking_state"]="manual";
                    Record(s,guard.Actor,"tracking.manual",id,content);
                    return true;
                });
                return Ok(new{status="ok"});
            }
            catch(KeyNotFoundException){return Fail(404,"订单不存在");}
            catch(Exception e){return Fail(409,e.Message);}
    }

    // POST /api/admin/orders/{id}/tracking/refresh
    /// <summary>
    /// 从快递服务查询并刷新物流状态。
    /// </summary>
    public async Task<IResult> RefreshTracking(string id, HttpContext ctx)
    {
            var guard=await Authorized(ctx,auth);if(guard.Error!=null)return guard.Error;
            if(cfg.Kuaidi100Key=="" || cfg.Kuaidi100Customer=="")return Fail(503,"未配置快递100查询接口凭证");
            var order=await db.Read(s=>s.Orders.TryGetValue(id,out var o)?Doc.Copy(o):null);
            if(order==null)return Fail(404,"订单不存在");
            if(Doc.Str(order,"status") is not ("shipped" or "completed"))return Fail(409,"订单尚未发货");
            var code=Doc.Str(order,"carrier_code");var number=Doc.Str(order,"tracking");
            if(code==""||number=="")return Fail(409,"请先录入快递公司和运单号");
            if(DateTimeOffset.TryParse(Doc.Str(order,"tracking_checked_at"),out var last) &&
               DateTimeOffset.UtcNow-last.ToUniversalTime()<TimeSpan.FromMinutes(30))
                return Fail(429,"物流接口至少间隔30分钟查询一次");
            // Same request fields and MD5 signature as Go Kuaidi100 real-time query.
            var param=new JsonObject{
                ["com"]=code,["num"]=number,["phone"]=Doc.Str(order,"phone"),["order"]="desc"
            }.ToJsonString();
            var signature=Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(param+cfg.Kuaidi100Key+cfg.Kuaidi100Customer)));
            JsonObject? result;
            try
            {
                using var request=new HttpRequestMessage(HttpMethod.Post,"https://poll.kuaidi100.com/poll/query.do")
                {
                    Content=new FormUrlEncodedContent(new Dictionary<string,string>{
                        ["customer"]=cfg.Kuaidi100Customer,["sign"]=signature,["param"]=param
                    })
                };
                using var response=await http.CreateClient("kuaidi").SendAsync(request);
                var payload=await response.Content.ReadAsStringAsync();
                result=JsonNode.Parse(payload) as JsonObject;
                if(!response.IsSuccessStatusCode || Doc.Str(result,"status")!="200")
                    return Fail(502,"物流平台暂无法查询");
            }
            catch{return Fail(502,"物流平台暂不可访问");}
            var state=Doc.Str(result,"state");
            var isCheck=Doc.Str(result,"ischeck");
            var list=Doc.Array(result,"data").Where(v=>Doc.Str(v,"time")!="" && Doc.Str(v,"context")!="")
                .Take(100).Select(v=>new JsonObject {
                    ["time"]=Doc.Str(v,"time"),["context"]=Doc.Str(v,"context"),["source"]="kuaidi100"
                }).OrderByDescending(v=>Doc.Str(v,"time")).ToArray();
            try
            {
                await db.Update(s=>
                {
                    if(!s.Orders.TryGetValue(id,out var o))throw new KeyNotFoundException();
                    if(Doc.Str(o,"tracking")!=number || Doc.Str(o,"carrier_code")!=code)
                        throw new InvalidOperationException("运单信息已变化，请重试");
                    o["tracking_events"]=new JsonArray(list.Select(v=>(JsonNode?)v.DeepClone()).ToArray());
                    o["tracking_checked_at"]=Doc.Now();
                    o["tracking_state"]=state=="3" || isCheck=="1"?"delivered":state;
                    if(state=="3" || isCheck=="1")o["delivered_at"]=Doc.Now();
                    Record(s,guard.Actor,"tracking.refreshed",id,list.Length+" events");
                    return true;
                });
                return Ok(new{events=list,tracking_state=state});
            }
            catch{return Fail(409,"物流信息写入失败，请刷新后重试");}
    }

}
