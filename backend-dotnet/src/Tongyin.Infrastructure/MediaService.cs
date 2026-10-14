using System.Security.Cryptography;
using System.Text;

namespace Tongyin.Api.Core;

public sealed class MediaService(Settings cfg)
{
    private string Root => Path.Combine(cfg.DataDir, "media");
    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private byte[] Sign(string payload) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg.SessionSecret), Encoding.UTF8.GetBytes(payload));
    public string Signed(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var exp = DateTimeOffset.UtcNow.AddHours(48).ToUnixTimeSeconds().ToString();
        return cfg.PublicUrl + "/media/" + name + "?expires=" + exp + "&signature=" + B64(Sign(name + ":" + exp));
    }
    public bool Verify(string file, string? expires, string? signature)
    {
        if (string.IsNullOrEmpty(file) || file.Length > 100 || Path.GetFileName(file)!=file ||
            file.Contains("..") || file.Contains('/') || file.Contains('\\') ||
            !long.TryParse(expires,out var ts) || DateTimeOffset.UtcNow.ToUnixTimeSeconds()>ts ||
            string.IsNullOrEmpty(signature)) return false;
        var expected = Encoding.ASCII.GetBytes(B64(Sign(file+":"+expires)));
        var given = Encoding.ASCII.GetBytes(signature);
        return expected.Length == given.Length && CryptographicOperations.FixedTimeEquals(expected,given);
    }
    public async Task<(string Name,string Mime)> Save(IFormFile file, bool imageOnly, CancellationToken ct=default)
    {
        var max = imageOnly ? 10L << 20 : 100L << 20;
        if (file.Length < 4 || file.Length > max) throw new ArgumentException("上传文件为空或超过限制");
        await using var stream = file.OpenReadStream();
        var head = new byte[512];
        var read = await stream.ReadAsync(head,ct);
        var mime = MediaMime(head.AsSpan(0,read), imageOnly);
        if (mime == "") throw new ArgumentException(imageOnly ? "图片必须是 JPG、PNG 或 WebP" : "模型必须是 GLB");
        var ext = mime switch { "image/jpeg"=>".jpg", "image/png"=>".png", "image/webp"=>".webp", _=>".glb" };
        var name=Doc.NewId()+ext;
        Directory.CreateDirectory(Root);
        var output=Path.Combine(Root,name);
        try
        {
            await using var dest = new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
            await dest.WriteAsync(head.AsMemory(0,read),ct);
            await stream.CopyToAsync(dest,ct);
        }
        catch { File.Delete(output); throw; }
        return (name,mime);
    }
    public string Absolute(string name) => Path.Combine(Root,name);
    public static string MediaMime(ReadOnlySpan<byte> head,bool imageOnly)
    {
        if (!imageOnly && head.Length >= 4 && head[..4].SequenceEqual("glTF"u8)) return "model/gltf-binary";
        if (imageOnly && head.Length>=8 && head[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) return "image/png";
        if (imageOnly && head.Length>=3 && head[0]==0xFF && head[1]==0xD8 && head[2]==0xFF) return "image/jpeg";
        if (imageOnly && head.Length>=12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return "";
    }
    public async Task<IResult> Serve(string file,string? exp,string? signature,CancellationToken ct=default)
    {
        if (!Verify(file,exp,signature)) return Results.NotFound();
        var path=Absolute(file);
        if (!File.Exists(path)) return Results.NotFound();
        var ext=Path.GetExtension(file).ToLowerInvariant();
        var mime=ext switch { ".png"=>"image/png", ".jpg"=>"image/jpeg", ".webp"=>"image/webp", ".glb"=>"model/gltf-binary", _=>"application/octet-stream"};
        await Task.CompletedTask;
        return Results.File(path,mime, enableRangeProcessing: true);
    }
}
