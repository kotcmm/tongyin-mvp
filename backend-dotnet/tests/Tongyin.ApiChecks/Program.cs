using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Tongyin.Api.Core;

namespace Tongyin.ApiChecks;

internal static class IntegrationMain
{
public static async Task Main()
{
var root=Path.Combine(Path.GetTempPath(),"ty-api-check-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("DEV_MODE","true");
Environment.SetEnvironmentVariable("DB_DRIVER","file");
Environment.SetEnvironmentVariable("DATA_DIR",root);
Environment.SetEnvironmentVariable("LISTEN_ADDR",":18081");
Environment.SetEnvironmentVariable("PUBLIC_URL","http://localhost:18081");
Environment.SetEnvironmentVariable("SESSION_SECRET","dotnet-compat-tests-a-long-secret-123");
Environment.SetEnvironmentVariable("ADMIN_BOOTSTRAP_USER","admin");
Environment.SetEnvironmentVariable("ADMIN_BOOTSTRAP_PASSWORD","local-test-password-123456");
Environment.SetEnvironmentVariable("AI_WEBHOOK_TOKEN","integration-webhook-secret");
try
{
    await using var app=new LocalApiFactory();
    using var client=app.CreateClient();
    JsonObject Parse(string s)=>JsonNode.Parse(s) as JsonObject ?? throw new Exception("missing JSON");

    var health=await client.GetAsync("/health");
    Must(health.StatusCode==HttpStatusCode.OK,"health");
    // All three URLs must match one endpoint, without AmbiguousMatchException.
    foreach(var adminPath in new[]{"/admin","/admin/","/admin/index.html"})
    {
        using var adminHome=await client.GetAsync(adminPath);
        Must(adminHome.StatusCode==HttpStatusCode.OK,
            $"legacy web admin entry {adminPath} (HTTP {(int)adminHome.StatusCode}; URL {adminHome.RequestMessage?.RequestUri})");
        var html=await adminHome.Content.ReadAsStringAsync();
        Must(html.Contains("童印运营中心"),"legacy admin HTML content "+adminPath);
    }
    foreach(var asset in new[]{"/admin/core.js","/admin/views.js","/admin/ai-admin.js",
        "/admin/actions.js","/admin/app.css","/admin/3d/index.html",
        "/admin/vendor/three.module.min.js","/admin/vendor/preview.mjs"})
    {
        var assetResponse=await client.GetAsync(asset);
        Must(assetResponse.StatusCode==HttpStatusCode.OK,
            $"legacy admin asset {asset} (HTTP {(int)assetResponse.StatusCode})");
        var contentType=assetResponse.Content.Headers.ContentType?.MediaType ?? "";
        if(asset.EndsWith(".js") || asset.EndsWith(".mjs"))
            Must(contentType.Contains("javascript"),"JavaScript MIME for "+asset);
    }

    var login=await client.PostAsJsonAsync("/api/admin/auth/login",new {username="admin",password="local-test-password-123456"});
    Must(login.StatusCode==HttpStatusCode.OK,"admin login");
    var adminToken=Doc.Str(Parse(await login.Content.ReadAsStringAsync()),"token");
    Must(adminToken.StartsWith("admin1."),"legacy token contract");
    var permissionDb=app.Services.GetRequiredService<IStore>();
    var actorTokens=await permissionDb.Update(s=>
    {
        var roles=new Dictionary<string,string>();
        foreach(var role in new[]{"service","operator","production","shipping"})
        {
            var person=CompatAuth.NewAdmin("role-"+role,"role-test-password-123456",role);
            s.Admins[Doc.Str(person,"owner")]=person;
            roles[role]=app.Services.GetRequiredService<CompatAuth>().AdminToken(person);
        }
        return roles;
    });
    foreach(var role in new[]{"service","operator","production","shipping"})
    {
        using var staff=app.CreateClient();
        staff.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",actorTokens[role]);
        var users=await staff.GetAsync("/api/admin/users");
        Must(users.StatusCode==(role is "service" or "operator" ? HttpStatusCode.OK : HttpStatusCode.Forbidden),
            "Go admin read permission "+role);
        var aiConfig=await staff.GetAsync("/api/admin/ai/config");
        Must(aiConfig.StatusCode==HttpStatusCode.Forbidden,"non-owner cannot read secrets "+role);
        var userStatus=await staff.PostAsJsonAsync("/api/admin/users/unknown/status",new{disabled=true});
        Must(userStatus.StatusCode==HttpStatusCode.Forbidden,"non-owner cannot disable user "+role);
        var worksView=await staff.GetAsync("/api/admin/works");
        Must(worksView.StatusCode==HttpStatusCode.OK,"staff works list permission "+role);
    }
    client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",adminToken);
    var invalidPage=await client.GetAsync("/api/admin/search/works?page_size=999");
    Must(invalidPage.StatusCode==HttpStatusCode.BadRequest,"Go search page-size validation");
    var badStatus=await client.GetAsync("/api/admin/search/orders?status=made_up");
    Must(badStatus.StatusCode==HttpStatusCode.BadRequest,"Go search status validation");


    var userLogin=await client.PostAsJsonAsync("/api/auth/wechat",new{code="DEV_LOGIN"});
    Must(userLogin.StatusCode==HttpStatusCode.OK,"wechat dev login");
    var token=Doc.Str(Parse(await userLogin.Content.ReadAsStringAsync()),"token");
    client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    var me=await client.GetAsync("/api/me");
    Must(me.StatusCode==HttpStatusCode.OK,"customer identity");
    var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9av94L8AAAAASUVORK5CYII=");
    MultipartFormDataContent Form(byte[] data,string filename)
    {
        var content=new MultipartFormDataContent();
        var file=new ByteArrayContent(data);file.Headers.ContentType=new MediaTypeHeaderValue(filename.EndsWith(".glb")?"model/gltf-binary":"image/png");
        content.Add(file,"file",filename);
        return content;
    }
    var create=await client.PostAsync("/api/works",Form(png,"draw.png"));
    Must(create.StatusCode==HttpStatusCode.Created,"original upload");
    var work=Parse(await create.Content.ReadAsStringAsync());
    var wid=Doc.Str(work,"id");
    Must(Doc.Str(work,"status")=="image_pending","initial workflow");
    var url=new Uri(Doc.Str(work,"original"));
    var original=await client.GetAsync(url.PathAndQuery);
    Must(original.StatusCode==HttpStatusCode.OK,"signed original media");
    Must((await original.Content.ReadAsByteArrayAsync()).SequenceEqual(png),"media bytes");
    var invalidMedia=await client.GetAsync(url.AbsolutePath+"?expires=4102444800&signature=fake");
    Must(invalidMedia.StatusCode==HttpStatusCode.NotFound,"signed media cannot be tampered with");

    var other=(CompatAuth)app.Services.GetRequiredService<CompatAuth>();
    client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",other.CustomerToken("another-user"));
    var forbidden=await client.GetAsync("/api/works/"+wid);
    Must(forbidden.StatusCode==HttpStatusCode.NotFound,"cross-customer work privacy");
    client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);

    var submit=await client.PostAsJsonAsync("/api/works/"+wid+"/generate",new{});
    Must(submit.StatusCode==HttpStatusCode.Accepted,"work generation queued");
    // No upstream AI call: admin uploads verified fallback image and 3D model.
    client.DefaultRequestHeaders.Authorization=null;
    client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",adminToken);
    var aiGet=await client.GetAsync("/api/admin/ai/config");
    Must(aiGet.StatusCode==HttpStatusCode.OK,"AI settings owner");
    var provider=await client.PostAsJsonAsync("/api/admin/ai/providers",new {
        name="image-test",stage="image",adapter="seedream",endpoint="https://ark.cn-beijing.volces.com/api/v3/images/generations",
        model="doubao-seedream-5-0-pro-260628",api_key="secret-for-test",enabled=false,@default=false
    });
    Must(provider.StatusCode==HttpStatusCode.OK,"AI provider persisted");

    var uploaded=await client.PostAsync("/api/admin/works/"+wid+"/asset?kind=candidate",Form(png,"candidate.png"));
    Must(uploaded.StatusCode==HttpStatusCode.OK,"admin candidate");
    client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    var selected=await client.PostAsJsonAsync("/api/works/"+wid+"/select",new{index=0});
    Must(selected.StatusCode==HttpStatusCode.Accepted,"select single candidate");

    client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",adminToken);
    var glb=Encoding.ASCII.GetBytes("glTF-test-model");
    var model=await client.PostAsync("/api/admin/works/"+wid+"/asset?kind=model",Form(glb,"model.glb"));
    Must(model.StatusCode==HttpStatusCode.OK,"admin GLB upload");

    client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    var order=await client.PostAsJsonAsync("/api/orders",new {
        work_id=wid,name="测试家长",phone="13800138000",address="广东省广州市天河区测试路100号"
    });
    Must(order.StatusCode==HttpStatusCode.Created,"order from GLB");
    var oid=Doc.Str(Parse(await order.Content.ReadAsStringAsync()),"id");
    var duplicated=await client.PostAsJsonAsync("/api/orders",new {
        work_id=wid,name="测试家长",phone="13800138000",address="广东省广州市天河区测试路100号"
    });
    Must(duplicated.StatusCode==HttpStatusCode.Conflict,"prevent duplicate orders");
    var payment=await client.PostAsJsonAsync("/api/orders/"+oid+"/pay",new{});
    Must(payment.StatusCode==HttpStatusCode.ServiceUnavailable,"payment forbidden until configured");
    var cancel=await client.PostAsJsonAsync("/api/orders/"+oid+"/cancel",new{reason="想先看看"});
    Must(cancel.StatusCode==HttpStatusCode.OK,"cancel unpaid order");
    var canceled=Parse(await cancel.Content.ReadAsStringAsync());
    Must(Doc.Str(canceled,"status")=="canceled","canceled order state");
    var notify=await client.PostAsJsonAsync("/api/internal/ai-result",new {
        work_id=wid,stage="image",generation_id="old-generation",urls=new[]{"https://example.com/result.png"}
    });
    Must(notify.StatusCode==HttpStatusCode.Unauthorized,"AI callback secret required");

    // Verify manager screens never expose OpenID or encrypted credentials.
    client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",adminToken);
    var list=await client.GetStringAsync("/api/admin/search/works?page=1&page_size=10");
    Must(!list.Contains("another-user") && !list.Contains("openid-one"),"admin page privacy");
    Console.WriteLine("PASS: live ASP.NET API login, signed upload, ownership, manual image/GLB, order, payment guard, cancellation, admin AI config");
}
finally{Directory.Delete(root,true);}
}
private static void Must(bool b,string reason){if(!b)throw new InvalidOperationException("FAIL "+reason);}
}

file sealed class LocalApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // WAF defaults to <solution>/<assembly name>; the project actually lives under src/.
        builder.UseContentRoot(Path.GetFullPath("backend-dotnet/src/Tongyin.Api"));
        base.ConfigureWebHost(builder);
    }
}
