using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tongyin.Api.Core;

namespace Tongyin.PayChecks;

internal static class PaymentIntegration
{
    public static async Task Main()
    {
        var temp=Path.Combine(Path.GetTempPath(),"ty-pay-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        using var platformKey=RSA.Create(2048);
        using var merchantKey=RSA.Create(2048);
        var publicPath=Path.Combine(temp,"platform.pem");
        var merchantPath=Path.Combine(temp,"merchant.pem");
        await File.WriteAllTextAsync(publicPath,platformKey.ExportSubjectPublicKeyInfoPem());
        await File.WriteAllTextAsync(merchantPath,merchantKey.ExportPkcs8PrivateKeyPem());
        var symmetricKey="0123456789abcdef0123456789abcdef";
        Environment.SetEnvironmentVariable("DEV_MODE","true");
        Environment.SetEnvironmentVariable("DB_DRIVER","file");
        Environment.SetEnvironmentVariable("DATA_DIR",temp);
        Environment.SetEnvironmentVariable("LISTEN_ADDR",":18082");
        Environment.SetEnvironmentVariable("PUBLIC_URL","http://localhost:18082");
        Environment.SetEnvironmentVariable("SESSION_SECRET","pay-test-secret-longer-than-24-bytes");
        Environment.SetEnvironmentVariable("ADMIN_BOOTSTRAP_PASSWORD","payment-test-admin-password");
        Environment.SetEnvironmentVariable("WX_APP_ID","wx-fake-appid");
        Environment.SetEnvironmentVariable("WX_MCH_ID","test-mchid");
        Environment.SetEnvironmentVariable("WX_MCH_SERIAL","merchant-serial");
        Environment.SetEnvironmentVariable("WX_MCH_PRIVATE_KEY",merchantPath);
        Environment.SetEnvironmentVariable("WX_PLATFORM_PUBLIC_KEY",publicPath);
        Environment.SetEnvironmentVariable("WX_PLATFORM_SERIAL","platform-serial");
        Environment.SetEnvironmentVariable("WX_PAY_API_V3_KEY",symmetricKey);
        Environment.SetEnvironmentVariable("WX_PAY_NOTIFY_URL","https://example.invalid/api/pay/notify");
        try
        {
            await using var server=new PayApiFactory();
            using var client=server.CreateClient();
            var store=server.Services.GetRequiredService<IStore>();
            await store.Update(s=>
            {
                s.Orders["test-order"]=new JsonObject {
                    ["id"]="test-order",["owner"]="openid-pay-check",["work_id"]="work-1",
                    ["created_at"]=Doc.Now(),["price_fen"]=12900,
                    ["status"]="awaiting_payment",["history"]=new JsonArray()
                };
                return true;
            });
            string EncryptTransaction(int price)
            {
                var value=new JsonObject{
                    ["out_trade_no"]="test-order",["trade_state"]="SUCCESS",
                    ["appid"]="wx-fake-appid",["mchid"]="test-mchid",
                    ["amount"]=new JsonObject{["total"]=price},
                    ["payer"]=new JsonObject{["openid"]="openid-pay-check"}
                }.ToJsonString();
                var nonce=Encoding.UTF8.GetBytes("nonce1234567");
                var plain=Encoding.UTF8.GetBytes(value);
                var encrypted=new byte[plain.Length];
                var tag=new byte[16];
                using(var aes=new AesGcm(Encoding.UTF8.GetBytes(symmetricKey),16))
                    aes.Encrypt(nonce,plain,encrypted,tag,Encoding.UTF8.GetBytes("extra"));
                return new JsonObject {
                    ["event_type"]="TRANSACTION.SUCCESS",
                    ["resource"]=new JsonObject{
                        ["algorithm"]="AEAD_AES_256_GCM",["nonce"]="nonce1234567",
                        ["associated_data"]="extra",
                        ["ciphertext"]=Convert.ToBase64String(encrypted.Concat(tag).ToArray())
                    }
                }.ToJsonString();
            }
            async Task<HttpStatusCode> Notify(string json,bool validSignature=true)
            {
                var timestamp=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                const string nonce="pay-notification";
                var toSign=timestamp+"\n"+nonce+"\n"+json+"\n";
                var sig=Convert.ToBase64String(platformKey.SignData(
                    Encoding.UTF8.GetBytes(toSign),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
                if(!validSignature)sig="invalid";
                using var req=new HttpRequestMessage(HttpMethod.Post,"/api/pay/notify"){
                    Content=new StringContent(json,Encoding.UTF8,"application/json")
                };
                req.Headers.TryAddWithoutValidation("Wechatpay-Timestamp",timestamp);
                req.Headers.TryAddWithoutValidation("Wechatpay-Nonce",nonce);
                req.Headers.TryAddWithoutValidation("Wechatpay-Serial","platform-serial");
                req.Headers.TryAddWithoutValidation("Wechatpay-Signature",sig);
                using var response=await client.SendAsync(req);
                return response.StatusCode;
            }
            var correct=EncryptTransaction(12900);
            Check(await Notify(correct,false)==HttpStatusCode.Unauthorized,"forged signature rejected");
            Check(await Notify(EncryptTransaction(1))==HttpStatusCode.Conflict,"forged amount rejected");
            Check(await store.Read(s=>Doc.Str(s.Orders["test-order"],"status"))=="awaiting_payment",
                "forged notifications have no side effects");
            Check(await Notify(correct)==HttpStatusCode.OK,"valid encrypted WeChat notification");
            Check(await Notify(correct)==HttpStatusCode.OK,"duplicate WeChat notification is idempotent");
            var result=await store.Read(s=>Doc.Copy(s.Orders["test-order"]));
            Check(Doc.Str(result,"status")=="paid","valid payment makes order paid");
            Check(Doc.Array(result,"history").Count==1,"payment callback never duplicates history");
            Console.WriteLine("PASS: WeChat pay RSA signature, AES-GCM decode, amount check, duplicate notification idempotency");
        }
        finally {Directory.Delete(temp,true);}
    }
    private static void Check(bool ok,string why)
    {
        if(!ok)throw new InvalidOperationException("FAIL: "+why);
    }
}
file sealed class PayApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.GetFullPath("backend-dotnet/src/Tongyin.Api"));
        base.ConfigureWebHost(builder);
    }
}
