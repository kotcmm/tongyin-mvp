using System.Text.Json.Nodes;
using System.Data.Common;
using Npgsql;
using MySqlConnector;
using Tongyin.Api.Core;

var postgres=Environment.GetEnvironmentVariable("TEST_POSTGRES_DSN")
    ?? "postgres://tongyin:test_password@127.0.0.1:5432/tongyin?sslmode=disable";
var mysql=Environment.GetEnvironmentVariable("TEST_MYSQL_DSN")
    ?? "tongyin:test_password@tcp(127.0.0.1:3306)/tongyin?charset=utf8mb4&parseTime=true";

foreach(var (driver,dsn) in new[]{("postgres",postgres),("mysql",mysql)})
{
    var db=new SqlStore(driver,dsn);
    await db.Initialize();
    // Seed a row using the old Go ty_works schema WITHOUT the new repository.
    // Then prove C# reads it and preserves fields Go did not explicitly model.
    var goId=Doc.NewId();
    var legacyJson=new JsonObject {
        ["id"]=goId,["owner"]="legacy-openid",["created_at"]=Doc.Now(),
        ["original"]="legacy-kid.png",["selected"]=-1,["candidates"]=new JsonArray(),
        ["preview"]=new JsonArray(),["status"]="image_pending",["go_custom_unknown"]="untouched"
    }.ToJsonString();
    await using (DbConnection legacyConn=driver=="postgres"
        ? new NpgsqlConnection("Host=127.0.0.1;Port=5432;Username=tongyin;Password=test_password;Database=tongyin;SSL Mode=Disable")
        : new MySqlConnection("Server=127.0.0.1;Port=3306;User ID=tongyin;Password=test_password;Database=tongyin"))
    {
        await legacyConn.OpenAsync();
        await using var cmd=legacyConn.CreateCommand();
        cmd.CommandText="INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES(@id,@owner,@created,@payload)";
        foreach(var (key,value) in new[]{("id",goId),("owner","legacy-openid"),("created",Doc.Now()),("payload",legacyJson)})
        {
            var p=cmd.CreateParameter();p.ParameterName="@"+key;p.Value=value;cmd.Parameters.Add(p);
        }
        await cmd.ExecuteNonQueryAsync();
    }
    var old=await db.Read(s=>s.Works.TryGetValue(goId,out var w)?Doc.Copy(w):null);
    if(old==null || Doc.Str(old,"go_custom_unknown")!="untouched")
        throw new Exception(driver+": existing legacy Go SQL row is unreadable");
    await db.Update(s=>{s.Works[goId]["status"]="image_ready";return true;});
    var retained=await db.Read(s=>s.Works[goId]);
    if(Doc.Str(retained,"original")!="legacy-kid.png" || Doc.Str(retained,"go_custom_unknown")!="untouched")
        throw new Exception(driver+": changed field on legacy row was lost");


    var id=Doc.NewId();
    var owner="openid-legacy";
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
