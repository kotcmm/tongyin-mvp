using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Tongyin.Api.Core;

// Port of Go's per-stage Seedream / async Worker dispatch. It deliberately does
// not auto-retry requests with uncertain upstream billing.
/// <summary>
/// 负责 AI 生成配置、密钥、任务派发及服务回调。
/// </summary>
public sealed class AiService(Settings cfg,IStore store,MediaService media,IHttpClientFactory factory,ILogger<AiService> logger)
{
    public static string DefaultPrompt(string stage) => stage=="model"
        ? "将参考卡通形象转换为单个可3D打印的完整角色模型，保留脸部和服饰的核心辨识特征，结构完整稳定，避免悬浮部件、过薄的四肢和无法打印的细节；输出可预览的 GLB 模型。"
        : "严格参考上传的儿童画，保留原画主体的轮廓、颜色、标志性装饰和稚拙特色，转换成单个全身、正面、立体感清晰的可爱卡通角色；纯净背景，造型简单完整，避免细小悬空零件，适合进一步生成3D打印白模。只输出一张图片，不添加文字、水印或额外角色。";
    /// <summary>
    /// 查找当前生图或三维模型提示词。
    /// </summary>
    public static JsonObject PromptOf(Snapshot s,string stage) =>
        s.AIPrompts.TryGetValue(stage,out var p)?Doc.Copy(p):
            new JsonObject{["id"]=stage,["version"]=1,["template"]=DefaultPrompt(stage)};
    public static JsonObject? ProviderOf(Snapshot s,string stage) =>
        s.AIProviders.Values.FirstOrDefault(p=>Doc.Str(p,"stage")==stage && Doc.Bool(p,"enabled") && Doc.Bool(p,"default")) is { } p
            ? Doc.Copy(p) : null;
    private byte[] KeyBytes => SHA256.HashData(Encoding.UTF8.GetBytes("tongyin-ai-api-key-v1:"+cfg.SessionSecret));
    /// <summary>
    /// 加密存储外部模型服务密钥。
    /// </summary>
    public string EncryptKey(string secret)
    {
        var nonce=RandomNumberGenerator.GetBytes(12);
        var data=Encoding.UTF8.GetBytes(secret);
        var cipher=new byte[data.Length];
        var tag=new byte[16];
        using var gcm=new AesGcm(KeyBytes,16);
        gcm.Encrypt(nonce,data,cipher,tag);
        return Convert.ToBase64String(nonce.Concat(cipher).Concat(tag).ToArray()).TrimEnd('=');
    }
    /// <summary>
    /// 解密已保存的模型服务密钥。
    /// </summary>
    public string DecryptKey(string encrypted)
    {
        if(encrypted=="")throw new InvalidOperationException("尚未配置 API Key");
        var base64=encrypted.PadRight((encrypted.Length+3)/4*4,'=');
        var data=Convert.FromBase64String(base64);
        if(data.Length<28)throw new InvalidOperationException("API Key 密文损坏");
        var plain=new byte[data.Length-28];
        using var gcm=new AesGcm(KeyBytes,16);
        try {gcm.Decrypt(data[..12],data[12..^16],data[^16..],plain);}
        catch(CryptographicException){throw new InvalidOperationException("AI 密钥无法解密，请检查 SESSION_SECRET");}
        return Encoding.UTF8.GetString(plain);
    }
    /// <summary>
    /// 判断当前生成阶段是否支持自动生成。
    /// </summary>
    public async Task<bool> Enabled(string stage) =>
        await store.Read(s=>ProviderOf(s,stage)!=null) || cfg.AiWorkerUrl.Length>0;
    public void Dispatch(JsonObject work,string stage)
    {
        // Keep all persistent job state in the database before spawning any work.
        _ = Task.Run(async () =>
        {
            try {await Generate(work,stage);}
            catch(Exception ex)
            {
                var wid=Doc.Str(work,"id");
                logger.LogWarning("AI dispatch failed work={Work} stage={Stage} class={Class}",wid,stage,ex.GetType().Name);
                var expected=stage=="model"?"model_pending":"image_pending";
                var generation=Doc.Str(work,"generation_id");
                await store.Update(s=>
                {
                    if(s.Works.TryGetValue(wid,out var saved) &&
                       Doc.Str(saved,"generation_id")==generation && Doc.Str(saved,"status")==expected)
                    {
                        saved["status"]="failed";
                        saved["note"]="AI 生成失败："+Sanitize(ex.Message);
                    }
                    return true;
                });
            }
        });
    }
    private static string Sanitize(string message)
    {
        // No API keys or signed URLs are ever sent to this method.
        var trimmed=message.Replace('\r',' ').Replace('\n',' ');
        return trimmed.Length>200 ? trimmed[..200] : trimmed;
    }
    /// <summary>
    /// 按作品配置执行图像或模型生成。
    /// </summary>
    public async Task Generate(JsonObject work,string stage)
    {
        var pair=await store.Read(s=> (Provider:ProviderOf(s,stage),Prompt:PromptOf(s,stage)));
        var provider=pair.Provider;
        if(provider==null && cfg.AiWorkerUrl=="")return; // manual queue
        var id=Doc.Str(work,"id");
        var generation=Doc.Str(work,"generation_id");
        var source=stage=="image" ? Doc.Str(work,"original") :
            Doc.Array(work,"candidates").ElementAtOrDefault(Doc.Int(work,"selected"))?.ToString() ?? "";
        if(source=="")throw new InvalidOperationException("没有可用的参考图像");
        var pid=provider==null?"legacy-worker":Doc.Str(provider,"id");
        await store.Update(s=>
        {
            if(s.Works.TryGetValue(id,out var saved) && Doc.Str(saved,"generation_id")==generation)
            {
                saved["ai_provider_id"]=pid;
                saved["ai_prompt_version"]=Doc.Int(pair.Prompt,"version",1);
                saved["ai_task_stage"]=stage;
                saved["ai_started_at"]=Doc.Now();
            }
            return true;
        });
        if(provider!=null && Doc.Str(provider,"adapter")=="seedream")
            await GenerateSeedream(id,generation,source,provider,Doc.Str(pair.Prompt,"template"));
        else
            await SendWorker(id,generation,stage,source,provider,Doc.Str(pair.Prompt,"template"),
                Doc.Int(pair.Prompt,"version",1));
    }
    private async Task SendWorker(string id,string generation,string stage,string source,JsonObject? provider,string prompt,int version)
    {
        var endpoint=provider==null?cfg.AiWorkerUrl:Doc.Str(provider,"endpoint");
        var secret=provider==null?cfg.AiWorkerToken:DecryptKey(Doc.Str(provider,"api_key_cipher"));
        var body=new JsonObject {
            ["work_id"]=id,["stage"]=stage,["generation_id"]=generation,
            ["source_url"]=media.Signed(source),["callback_url"]=cfg.PublicUrl+"/api/internal/ai-result",
            ["instructions"]=prompt,["prompt_version"]=version,["model"]=provider==null?"":Doc.Str(provider,"model")
        };
        using var req=new HttpRequestMessage(HttpMethod.Post,endpoint){
            Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")
        };
        if(secret!="")req.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",secret);
        using var response=await factory.CreateClient("worker").SendAsync(req);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Worker HTTP "+(int)response.StatusCode);
    }
    private async Task GenerateSeedream(string id,string generation,string source,JsonObject provider,string prompt)
    {
        var key=DecryptKey(Doc.Str(provider,"api_key_cipher"));
        var bytes=await File.ReadAllBytesAsync(media.Absolute(source));
        if(bytes.Length>10<<20)throw new InvalidOperationException("原画体积超出10MiB");
        var mime=MediaService.MediaMime(bytes,true);
        if(mime=="")throw new InvalidOperationException("原画格式无效");
        var payload=new JsonObject{
            ["model"]=Doc.Str(provider,"model"),["prompt"]=prompt,
            ["image"]="data:"+mime+";base64,"+Convert.ToBase64String(bytes),
            ["size"]="2K",["response_format"]="url",["watermark"]=false
        };
        var trace=Doc.NewId();var sw=Stopwatch.StartNew();
        bool usesProxy;
        try {usesProxy=!HttpClient.DefaultProxy.IsBypassed(new Uri(Doc.Str(provider,"endpoint")));}
        catch {usesProxy=false;}
        logger.LogInformation("Seedream start trace={Trace} work={Work} body_bytes={Size} image_bytes={ImageSize} model={Model} proxy={Proxy} timeout_seconds=190",
            trace,id,Encoding.UTF8.GetByteCount(payload.ToJsonString()),bytes.Length,Doc.Str(provider,"model"),usesProxy);
        using var req=new HttpRequestMessage(HttpMethod.Post,Doc.Str(provider,"endpoint")){
            Content=new StringContent(payload.ToJsonString(),Encoding.UTF8,"application/json")
        };
        req.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",key);
        HttpResponseMessage response;
        try {response=await factory.CreateClient("ai").SendAsync(req);}
        catch(Exception e)
        {
            var cause=e;
            while(cause.InnerException!=null)cause=cause.InnerException;
            var errorKind=cause is System.Net.Sockets.SocketException socket
                ? "socket_"+socket.SocketErrorCode.ToString() : cause.GetType().Name;
            // Never print full exception strings: a proxy may embed credential-bearing URLs.
            logger.LogWarning("Seedream transport failed trace={Trace} work={Work} elapsed_ms={Ms} error_kind={ErrorKind} proxy={Proxy}",
                trace,id,sw.ElapsedMilliseconds,errorKind,usesProxy);
            throw new InvalidOperationException("Seedream 请求中断，诊断编号 "+trace+"；请先检查后台日志，暂勿立即重试");
        }
        using(response)
        {
            logger.LogInformation("Seedream response trace={Trace} work={Work} status={Status} elapsed_ms={Ms}",trace,id,(int)response.StatusCode,sw.ElapsedMilliseconds);
            if(!response.IsSuccessStatusCode)
            {
                var text=await response.Content.ReadAsStringAsync();
                var errorPayload=SafeParse(text);
                var code=Doc.Str(errorPayload?["error"],"code","UnknownError");
                // Do not persist untrusted upstream messages that may contain secret-bearing URLs.
                throw new InvalidOperationException("Seedream HTTP "+(int)response.StatusCode+" ("+new string(code.Where(c=>char.IsLetterOrDigit(c)||c=='.'||c=='_').Take(80).ToArray())+")");
            }
            var parsed=SafeParse(await response.Content.ReadAsStringAsync());
            var arr=Doc.Array(parsed,"data");
            if(arr.Count!=1 || Doc.Str(arr[0],"url")=="")throw new InvalidOperationException("Seedream 没有返回单张图片");
            var filename=await Download(Doc.Str(arr[0],"url"),true);
            try
            {
                await store.Update(s=>
                {
                    if(!s.Works.TryGetValue(id,out var w) || Doc.Str(w,"status")!="image_pending" ||
                        Doc.Str(w,"generation_id")!=generation)throw new InvalidOperationException("旧任务结果已失效");
                    w["candidates"]=new JsonArray(filename);
                    w["status"]="image_ready";
                    w["note"]="";
                    return true;
                });
            }
            catch {File.Delete(media.Absolute(filename));throw;}
            logger.LogInformation("Seedream image stored trace={Trace} work={Work} elapsed_ms={Ms}",trace,id,sw.ElapsedMilliseconds);
        }
    }
    private static JsonObject? SafeParse(string raw)
    {
        try{return JsonNode.Parse(raw) as JsonObject;}
        catch{return null;}
    }
    /// <summary>
    /// 执行ResultPath操作。
    /// </summary>
    public string ResultPath(string file) => media.Absolute(file);
    public async Task<string> Download(string url,bool image)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) ||
            uri.Scheme!="https" || uri.UserInfo!="" || uri.IsLoopback ||
            uri.Host.EndsWith(".local",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AI 结果 URL 必须是公网 HTTPS");
        if(System.Net.IPAddress.TryParse(uri.Host,out var ip) &&
            (System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal ||
             ip.IsIPv6SiteLocal || ip.GetAddressBytes()[0]==10 ||
             (ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork &&
              (ip.GetAddressBytes()[0]==127 || ip.GetAddressBytes()[0]==192 && ip.GetAddressBytes()[1]==168 ||
               ip.GetAddressBytes()[0]==172 && ip.GetAddressBytes()[1] is >=16 and <=31))))
            throw new InvalidOperationException("禁止从内网地址下载模型");
        using var req=new HttpRequestMessage(HttpMethod.Get,uri);
        using var res=await factory.CreateClient("download").SendAsync(req,HttpCompletionOption.ResponseHeadersRead);
        if(!res.IsSuccessStatusCode)throw new InvalidOperationException("下载 AI 结果失败 HTTP "+(int)res.StatusCode);
        var limit=image?10L<<20:100L<<20;
        if(res.Content.Headers.ContentLength>limit)throw new InvalidOperationException("AI 结果超出限制");
        await using var stream=await res.Content.ReadAsStreamAsync();
        using var buffer=new MemoryStream();
        var bytes=new byte[81920];int size;long count=0;
        while((size=await stream.ReadAsync(bytes))>0)
        {
            count+=size;if(count>limit)throw new InvalidOperationException("AI 结果超出限制");
            await buffer.WriteAsync(bytes.AsMemory(0,size));
        }
        var raw=buffer.ToArray();
        var mime=MediaService.MediaMime(raw,image);
        if(mime=="")throw new InvalidOperationException("AI 结果不是正确的图片或 GLB");
        var ext=mime switch{"image/png"=>".png","image/jpeg"=>".jpg","image/webp"=>".webp",_=>".glb"};
        var name=Doc.NewId()+ext;
        Directory.CreateDirectory(Path.Combine(cfg.DataDir,"media"));
        await File.WriteAllBytesAsync(media.Absolute(name),raw);
        return name;
    }
}
