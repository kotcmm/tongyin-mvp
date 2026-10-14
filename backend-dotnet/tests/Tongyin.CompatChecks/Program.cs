using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Tongyin.Api.Core;

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

    var cfg=new Settings{SessionSecret="test-secret-compatible-123456",DataDir=dir,PublicUrl="http://localhost:18081"};
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

    var document=Snapshot.FromJson(Doc.Parse(await File.ReadAllTextAsync(file)));
    Assert(document.Works.ContainsKey("w1"),"Go document model loading");
    Console.WriteLine("PASS: file store round-trip, unknown field, signed media, PBKDF2, Go snapshot layout");
}
finally {Directory.Delete(dir,true);}
static void Assert(bool condition,string reason)
{
    if(!condition)throw new Exception("FAIL: "+reason);
}
