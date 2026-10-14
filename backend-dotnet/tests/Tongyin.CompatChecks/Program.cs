using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

var dir=Path.Combine(Path.GetTempPath(),"ty-migration-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    var file=Path.Combine(dir,"store.json");
    var store=new FileStore(file);
    await store.Update(s=>
    {
        s.Works["w1"]=new JsonObject{
            ["id"]="w1",["owner"]="openid-one",["created_at"]="2026-10-14T14:40:00+08:00",
            ["original"]="old.png",["status"]="image_ready",["unknown_go_field"]="keep-it",
            ["candidates"]=new JsonArray("result.png")
        };
        return true;
    });
    await store.Update(s=>
    {
        s.Works["w1"]["status"]="model_pending";
        return true;
    });
    var persisted=await store.Read(s=>Doc.Copy(s.Works["w1"]));
    Assert(Doc.Str(persisted,"unknown_go_field")=="keep-it","unknown Go JSON fields must survive");
    Assert(Doc.Str(persisted,"status")=="model_pending","document update persisted");

    var cfg=new Settings{SessionSecret="dotnet-compat-tests-a-long-secret-123",DataDir=dir,PublicUrl="http://localhost:18081"};
    var media=new MediaService(cfg);
    var signed=media.Signed("old.png");
    var uri=new Uri(signed);
    var query=uri.Query.TrimStart('?').Split('&').Select(s=>s.Split('=',2))
        .ToDictionary(x=>x[0],x=>x[1]);
    Assert(media.Verify("old.png",query["expires"],query["signature"]),"signed media can be verified");
    Assert(!media.Verify("wrong.png",query["expires"],query["signature"]),"signed media prevents filename substitution");

    // Go implements PBKDF2-HMAC-SHA256 over the literal 32-char hex salt string.
    var salt="00112233445566778899aabbccddeeff";
    var computed=CompatAuth.PasswordHash("a-password-for-test",salt);
    var independent=Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(
        "a-password-for-test",Encoding.UTF8.GetBytes(salt),120000,HashAlgorithmName.SHA256,32)).ToLowerInvariant();
    Assert(computed==independent && computed.Length==64,"Go PBKDF2 compatibility");

    // Golden values are generated from the legacy Go algorithm using a fixed
    // synthetic secret / timestamp, not from the new C# implementation.
    Assert(computed=="0dfaa474694e0e429716ceb2709b18441be4b5c459fb1956d2654d5d12246b3e",
        "Go PBKDF2-HMAC-SHA256 fixed fixture");
    var fixedMediaSig="cXDf5odBSXwy3ZCTCYF8aEq2ZXOmiZ2yRSJp942wna4";
    Assert(media.Verify("original.png","4102444800",fixedMediaSig),"Go signed media fixed fixture");

    await store.Update(s =>
    {
        s.Admins["admin"]=new JsonObject {
            ["owner"]="admin",["role"]="owner",["version"]=1,["disabled"]=false,
            ["salt"]=salt,["password_hash"]=computed,["created_at"]=Doc.Now()
        };
        return true;
    });
    var compatibleAuth=new CompatAuth(cfg,store);
    var adminContext=new DefaultHttpContext();
    adminContext.Request.Headers.Authorization="Bearer admin1.YWRtaW58MXw0MTAyNDQ0ODAw.OYoN7OtGEMQY10PuqV8g7EDet9CqQS7eSu-E7idvIJw";
    var oldAdmin=await compatibleAuth.VerifyAdmin(adminContext);
    Assert(oldAdmin?.Name=="admin" && oldAdmin?.Role=="owner","Go admin session token fixed fixture");
    var customerContext=new DefaultHttpContext();
    customerContext.Request.Headers.Authorization="Bearer ZGV2LXVzZXJ8NDEwMjQ0NDgwMA.O8rBEkx2zZSRJ0cph1VARb7nr0avFu8ND4c4YgvCDSo";
    Assert(compatibleAuth.VerifyCustomer(customerContext)=="dev-user","Go customer session token fixed fixture");
    var legacyKey="AAECAwQFBgcICQoLII2Lue0b20wiM4sQLZ4hvzdWB2oNqneHlkUhNBY2pa7peNgQXA";
    var ai=new AiService(cfg,store,media,new LocalHttpFactory(),NullLogger<AiService>.Instance);
    Assert(ai.DecryptKey(legacyKey)=="test-seedream-api-key","Go AES-GCM API Key fixed fixture");

    await store.Update(s =>
    {
        s.Orders["legacy-order"]=new JsonObject
        {
            ["id"]="legacy-order",["owner"]="old-openid",["created_at"]=Doc.Now(),
            ["status"]="paid"
        };
        s.Works["legacy-model"]=new JsonObject
        {
            ["id"]="legacy-model",["owner"]="old-openid",["created_at"]=Doc.Now(),
            ["status"]="model_pending",["model"]="existing.glb"
        };
        return true;
    });
    var migrated=await LegacyDataMigration.Apply(store);
    Assert(migrated.Users==1 && migrated.Models==1,"Go startup data repairs");
    var migratedAgain=await LegacyDataMigration.Apply(store);
    Assert(migratedAgain.Users==0 && migratedAgain.Models==0,"Go data repair is idempotent");
    var ready=await store.Read(s=>Doc.Str(s.Works["legacy-model"],"status"));
    Assert(ready=="model_ready","legacy GLB is ready after migration");

    var document=Snapshot.FromJson(Doc.Parse(await File.ReadAllTextAsync(file)));
    Assert(document.Works.ContainsKey("w1"),"Go document model loading");
    Console.WriteLine("PASS: file store round-trip, unknown field, signed media, PBKDF2, Go snapshot layout");
}
finally {Directory.Delete(dir,true);}
static void Assert(bool condition,string reason)
{
    if(!condition)throw new Exception("FAIL: "+reason);
}

file sealed class LocalHttpFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
