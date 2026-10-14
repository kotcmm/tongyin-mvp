using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Tongyin.Api.Core;

// Preserve Go wire tokens and admin password hashes during migration.
public sealed class CompatAuth(Settings cfg, IStore db)
{
    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static byte[] Unb64(string value)
    {
        var x = value.Replace('-','+').Replace('_','/');
        return Convert.FromBase64String(x.PadRight((x.Length + 3) / 4 * 4, '='));
    }
    private byte[] Hmac(string text) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg.SessionSecret), Encoding.UTF8.GetBytes(text));
    private static bool SecureEqual(byte[] a, byte[] b) => a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a,b);
    public static string PasswordHash(string password, string saltHex) =>
        Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(password, Encoding.UTF8.GetBytes(saltHex), 120000, HashAlgorithmName.SHA256, 32)).ToLowerInvariant();
    public string CustomerToken(string owner)
    {
        var encoded = B64(Encoding.UTF8.GetBytes(owner + "|" + DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()));
        return encoded + "." + B64(Hmac(encoded));
    }
    public string AdminToken(JsonObject user)
    {
        var data = B64(Encoding.UTF8.GetBytes(Doc.Str(user,"owner")+"|"+Doc.Int(user,"version")+"|"+DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds()));
        return "admin1." + data + "." + B64(Hmac("admin1."+data));
    }
    public string? VerifyCustomer(HttpContext ctx)
    {
        try
        {
            var p = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal).Split('.');
            if (p.Length != 2 || !SecureEqual(Unb64(p[1]), Hmac(p[0]))) return null;
            var d = Encoding.UTF8.GetString(Unb64(p[0])).Split('|');
            return d.Length == 2 && d[0] != "" && long.TryParse(d[1],out var expires) &&
                   DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= expires ? d[0] : null;
        }
        catch { return null; }
    }
    public async Task<(string Name,string Role)?> VerifyAdmin(HttpContext ctx)
    {
        try
        {
            var legacy = ctx.Request.Headers["X-Admin-Token"].ToString();
            if (cfg.AdminToken.Length > 0 && SecureEqual(Encoding.UTF8.GetBytes(legacy), Encoding.UTF8.GetBytes(cfg.AdminToken))) return ("legacy-token","owner");
            var p = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal).Split('.');
            if (p.Length != 3 || p[0] != "admin1" || !SecureEqual(Unb64(p[2]),Hmac("admin1."+p[1]))) return null;
            var d = Encoding.UTF8.GetString(Unb64(p[1])).Split('|');
            if (d.Length != 3 || !int.TryParse(d[1],out var version) || !long.TryParse(d[2],out var expires) ||
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expires) return null;
            return await db.Read<(string,string)?>(state =>
            {
                if (!state.Admins.TryGetValue(d[0], out var admin) || Doc.Bool(admin,"disabled") ||
                    Doc.Int(admin,"version") != version) return null;
                return (d[0],Doc.Str(admin,"role"));
            });
        }
        catch { return null; }
    }
    public async Task<string?> Customer(HttpContext ctx)
    {
        var owner = VerifyCustomer(ctx);
        if (owner == null) return null;
        var exists = await db.Read(state => state.Users.TryGetValue(owner, out var user) ? user : null);
        if (exists == null)
        {
            await db.Update(state => {
                if (!state.Users.ContainsKey(owner)) state.Users[owner] = new JsonObject
                {
                    ["id"] = Doc.NewId(), ["owner"] = owner, ["created_at"] = Doc.Now(), ["last_login"] = Doc.Now()
                };
                return true;
            });
        }
        else if (Doc.Bool(exists,"disabled")) return null;
        return owner;
    }
    public async Task<IResult?> ProtectAdmin(HttpContext ctx, bool ownerOnly=false)
    {
        var admin = await VerifyAdmin(ctx);
        if (admin == null) return Results.Json(new { error="管理员未登录或会话已失效" },statusCode:401);
        if (ownerOnly && admin.Value.Role != "owner")
            return Results.Json(new { error="当前角色无权进行此操作" },statusCode:403);
        if (!ownerOnly && !AllowedAdminRoute(admin.Value.Role, ctx.Request))
            return Results.Json(new { error="当前角色无权进行此操作" },statusCode:403);
        return null;
    }
    // Go's adminAllowed() is the permission baseline for all operational roles.
    public static bool AllowedAdminRoute(string role, HttpRequest request)
    {
        if (role == "owner") return true;
        var path = request.Path.Value ?? "";
        if (request.Method == "GET")
        {
            if (path.StartsWith("/api/admin/dashboard", StringComparison.Ordinal) ||
                path.StartsWith("/api/admin/works", StringComparison.Ordinal) ||
                path.StartsWith("/api/admin/orders", StringComparison.Ordinal) ||
                path.StartsWith("/api/admin/me", StringComparison.Ordinal)) return true;
            return (role == "operator" || role == "service") &&
                path.StartsWith("/api/admin/users", StringComparison.Ordinal);
        }
        if (request.Method == "POST")
        {
            if (path.StartsWith("/api/admin/works/", StringComparison.Ordinal))
                return role is "operator" or "production";
            if (path.StartsWith("/api/admin/orders/", StringComparison.Ordinal))
                return role is "operator" or "shipping";
        }
        return false;
    }
    public async Task<IResult?> ProtectCustomer(HttpContext ctx) =>
        await Customer(ctx) is null ? Results.Json(new { error="请登录或账号已停用" },statusCode:401) : null;
    public static JsonObject NewAdmin(string username, string password, string role)
    {
        if (password.Length < 12) throw new ArgumentException("后台密码至少12位");
        if (username.Length < 3 || username.Length > 32 || username.IndexOfAny([' ','\t','\n','|','/'])>=0)
            throw new ArgumentException("账号格式不正确");
        if (role is not ("owner" or "operator" or "production" or "shipping" or "service"))
            throw new ArgumentException("角色不存在");
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return new JsonObject
        {
            ["owner"]=username,["created_at"]=Doc.Now(),["role"]=role,["salt"]=salt,
            ["password_hash"]=PasswordHash(password,salt),["version"]=1,["disabled"]=false
        };
    }
}
