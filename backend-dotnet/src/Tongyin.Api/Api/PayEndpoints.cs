using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using static Tongyin.Api.Api.ApiUtils;

namespace Tongyin.Api.Api;

public static class PayEndpoints
{
    private static RSA MerchantKey(string path)
    {
        var pem=File.ReadAllText(path);
        var rsa=RSA.Create();
        try {rsa.ImportFromPem(pem);return rsa;}
        catch {rsa.Dispose();throw;}
    }
    private static RSA PlatformKey(string path)
    {
        var pem=File.ReadAllText(path);
        if(pem.Contains("BEGIN CERTIFICATE",StringComparison.Ordinal))
        {
            using var cert=X509Certificate2.CreateFromPem(pem);
            return cert.GetRSAPublicKey()??throw new InvalidOperationException("平台证书不是 RSA");
        }
        var rsa=RSA.Create();
        try {rsa.ImportFromPem(pem);return rsa;}
        catch {rsa.Dispose();throw;}
    }
    public static string Sign(RSA rsa,string content) =>
        Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(content),
            HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
    public static bool Verify(RSA rsa,string content,string signature)
    {
        try{return rsa.VerifyData(Encoding.UTF8.GetBytes(content),Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);}
        catch(FormatException){return false;}
    }
    private static string NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    public static void MapPayment(this WebApplication app)
    {
        app.MapPost("/api/orders/{id}/pay",async (string id,HttpContext ctx,Settings cfg,CompatAuth auth,IStore db,IHttpClientFactory http)=>
        {
            var owner=await auth.Customer(ctx);if(owner==null)return Fail(401,"请登录");
            var order=await db.Read(s=>s.Orders.TryGetValue(id,out var o) && Doc.Str(o,"owner")==owner ? Doc.Copy(o):null);
            if(order==null)return Fail(404,"订单不存在");
            if(Doc.Str(order,"status")!="awaiting_payment")return Fail(409,"订单状态不允许支付");
            if(!cfg.PayReady)return Fail(503,"微信商户配置尚未完成，暂时不能付款");
            RSA rsa;
            try {rsa=MerchantKey(cfg.WxMchPrivateKeyPath);}
            catch{return Fail(503,"商户私钥配置错误");}
            using(rsa)
            {
                try
                {
                    await db.Update(s=>
                    {
                        if(!s.Orders.TryGetValue(id,out var o) || Doc.Str(o,"status")!="awaiting_payment")
                            throw new InvalidOperationException("订单状态已经变化，请刷新");
                        if(Doc.Str(o,"payment_started_at")=="")o["payment_started_at"]=Doc.Now();
                        return true;
                    });
                }
                catch{return Fail(409,"订单状态已经变化，请刷新");}
                var body=new JsonObject{
                    ["appid"]=cfg.WxAppId,["mchid"]=cfg.WxMchId,["description"]="童印 · 儿童画定制白模",
                    ["out_trade_no"]=id,["notify_url"]=cfg.WxPayNotifyUrl,
                    ["amount"]=new JsonObject{["total"]=Doc.Int(order,"price_fen"),["currency"]="CNY"},
                    ["payer"]=new JsonObject{["openid"]=owner}
                };
                var json=body.ToJsonString();
                const string path="/v3/pay/transactions/jsapi";
                var ts=NowUnix();var nonce=Doc.NewId();
                var signature=Sign(rsa,"POST\n"+path+"\n"+ts+"\n"+nonce+"\n"+json+"\n");
                var authValue=$"WECHATPAY2-SHA256-RSA2048 mchid=\"{cfg.WxMchId}\",nonce_str=\"{nonce}\",timestamp=\"{ts}\",serial_no=\"{cfg.WxMchSerial}\",signature=\"{signature}\"";
                try
                {
                    using var req=new HttpRequestMessage(HttpMethod.Post,"https://api.mch.weixin.qq.com"+path){
                        Content=new StringContent(json,Encoding.UTF8,"application/json")
                    };
                    req.Headers.TryAddWithoutValidation("Authorization",authValue);
                    req.Headers.Accept.ParseAdd("application/json");
                    using var response=await http.CreateClient("wechatpay").SendAsync(req);
                    if(!response.IsSuccessStatusCode)return Fail(502,"微信支付预下单失败 HTTP "+(int)response.StatusCode);
                    var payload=JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject;
                    var prepay=Doc.Str(payload,"prepay_id");
                    if(prepay=="")return Fail(502,"微信预支付数据不正确");
                    var clientTime=NowUnix();var clientNonce=Doc.NewId();var package="prepay_id="+prepay;
                    var clientSignature=Sign(rsa,cfg.WxAppId+"\n"+clientTime+"\n"+clientNonce+"\n"+package+"\n");
                    return Ok(new {timeStamp=clientTime,nonceStr=clientNonce,@package,signType="RSA",paySign=clientSignature});
                }
                catch{return Fail(502,"创建微信支付订单失败");}
            }
        });
        app.MapPost("/api/pay/notify",async (HttpContext ctx,Settings cfg,IStore db)=>
        {
            if(!cfg.PayReady)return Fail(503,"支付回调尚未配置");
            if(cfg.WxPlatformSerial!="" && ctx.Request.Headers["Wechatpay-Serial"]!=cfg.WxPlatformSerial)
                return Fail(401,"平台序列号不符");
            RSA platform;
            try{platform=PlatformKey(cfg.WxPlatformPublicKeyPath);}
            catch{return Fail(500,"平台验签密钥不可用");}
            using(platform)
            {
                var timestamp=ctx.Request.Headers["Wechatpay-Timestamp"].ToString();
                var nonce=ctx.Request.Headers["Wechatpay-Nonce"].ToString();
                var signature=ctx.Request.Headers["Wechatpay-Signature"].ToString();
                if(!long.TryParse(timestamp,out var unix))return Fail(401,"回调时间戳无效");
                var delta=(DateTimeOffset.UtcNow-DateTimeOffset.FromUnixTimeSeconds(unix)).Duration();
                if(delta>TimeSpan.FromMinutes(5))return Fail(401,"回调时间戳无效");
                if(ctx.Request.ContentLength > 1<<20)return Fail(400,"回调过大");
                using var buffer=new MemoryStream();
                try
                {
                    await ctx.Request.Body.CopyToAsync(buffer);
                    if(buffer.Length>1<<20)return Fail(400,"回调过大");
                }
                catch{return Fail(400,"回调读取失败");}
                var raw=Encoding.UTF8.GetString(buffer.ToArray());
                if(!Verify(platform,timestamp+"\n"+nonce+"\n"+raw+"\n",signature))
                    return Fail(401,"验签失败");
                JsonObject? eventData;
                try{eventData=JsonNode.Parse(raw) as JsonObject;}
                catch{return Fail(400,"回调内容无效");}
                if(Doc.Str(eventData,"event_type")!="TRANSACTION.SUCCESS")
                    return Ok(new {code="SUCCESS",message="成功"});
                var resource=eventData?["resource"] as JsonObject;
                if(Doc.Str(resource,"algorithm")!="AEAD_AES_256_GCM" || Encoding.UTF8.GetByteCount(cfg.WxPayApiKey)!=32)
                    return Fail(500,"回调加密配置错误");
                JsonObject? verified;
                try
                {
                    var data=Convert.FromBase64String(Doc.Str(resource,"ciphertext"));
                    if(data.Length<16)return Fail(400,"密文无效");
                    var plain=new byte[data.Length-16];
                    var nonceBytes=Encoding.UTF8.GetBytes(Doc.Str(resource,"nonce"));
                    using var aes=new AesGcm(Encoding.UTF8.GetBytes(cfg.WxPayApiKey),16);
                    aes.Decrypt(nonceBytes,data[..^16],data[^16..],plain,
                        Encoding.UTF8.GetBytes(Doc.Str(resource,"associated_data")));
                    verified=JsonNode.Parse(plain) as JsonObject;
                }
                catch{return Fail(401,"回调解密失败");}
                if(Doc.Str(verified,"trade_state")!="SUCCESS" || Doc.Str(verified,"appid")!=cfg.WxAppId ||
                   Doc.Str(verified,"mchid")!=cfg.WxMchId)return Fail(409,"交易商户信息不符");
                var trade=Doc.Str(verified,"out_trade_no");
                try
                {
                    await db.Update(s=>
                    {
                        if(!s.Orders.TryGetValue(trade,out var order))throw new KeyNotFoundException();
                        if(Doc.Str(order,"owner")!=Doc.Str(verified?["payer"],"openid") ||
                           Doc.Int(order,"price_fen")!=Doc.Int(verified?["amount"],"total"))
                            throw new InvalidOperationException("订单身份或金额不符");
                        var status=Doc.Str(order,"status");
                        if(status=="canceled")
                        {
                            order["status"]="paid";order["refund_status"]="requested";
                            History(order,"paid_after_cancel","需要核实并退款");
                            Record(s,"wechatpay","order.paid_after_cancel",trade,"待退款");
                        }
                        else if(status=="awaiting_payment")
                        {
                            order["status"]="paid";
                            History(order,"paid","微信支付通知已验签");
                            Record(s,"wechatpay","order.paid",trade,"微信支付验签通过");
                        }
                        return true;
                    });
                    return Ok(new {code="SUCCESS",message="成功"});
                }
                catch{return Fail(409,"未找到匹配的订单");}
            }
        });
    }
    private static void History(JsonObject o,string action,string note)
    {
        if(o["history"] is not JsonArray)o["history"]=new JsonArray();
        ((JsonArray)o["history"]!).Add(new JsonObject{["at"]=Doc.Now(),["actor"]="wechatpay",["action"]=action,["note"]=note});
    }
}
