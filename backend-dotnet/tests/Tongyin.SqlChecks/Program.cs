using System.Text.Json.Nodes;
using Tongyin.Api.Core;

var postgres=Environment.GetEnvironmentVariable("TEST_POSTGRES_DSN")
    ?? "postgres://tongyin:test_password@127.0.0.1:5432/tongyin?sslmode=disable";
var mysql=Environment.GetEnvironmentVariable("TEST_MYSQL_DSN")
    ?? "tongyin:test_password@tcp(127.0.0.1:3306)/tongyin?charset=utf8mb4&parseTime=true";

foreach(var (driver,dsn) in new[]{("postgres",postgres),("mysql",mysql)})
{
    var db=new SqlStore(driver,dsn);
    await db.Initialize();
    var id=Doc.NewId(), owner="openid-legacy";
    var legacy=new JsonObject {
        ["id"]=id,["owner"]=owner,["created_at"]=Doc.Now(),["original"]="go-source.png",
        ["selected"]=-1,["candidates"]=new JsonArray(),["preview"]=new JsonArray(),
        ["status"]="image_pending",["go_unknown_extra"]="keep-me"
    };
    await db.Update(s=>{
        s.Works[id]=legacy;
        s.Users[owner]=new JsonObject{
            ["id"]=Doc.NewId(),["owner"]=owner,["created_at"]=Doc.Now(),["last_login"]=Doc.Now()
        };
        return true;
    });
    var read=await db.Read(s=>s.Works.TryGetValue(id,out var item)?Doc.Copy(item):null);
    if(read==null || Doc.Str(read,"original")!="go-source.png")
        throw new Exception(driver+": created Go-compatible document not readable");

    await db.Update(s=>{
        s.Works[id]["status"]="image_ready";
        s.Works[id]["candidates"]=new JsonArray("card.png");
        return true;
    });
    var again=await db.Read(s=>Doc.Copy(s.Works[id]));
    if(Doc.Str(again,"status")!="image_ready" || Doc.Str(again,"go_unknown_extra")!="keep-me")
        throw new Exception(driver+": update lost an original Go field");
    var count=await db.Read(s=>s.Works.ContainsKey(id));
    if(!count)throw new Exception(driver+": work unexpectedly missing");

    Console.WriteLine("PASS "+driver+" SQL schema and cross-language JSON document round trip");
}
