using System.Text.Json.Nodes;
using System.Text.Json;

namespace Tongyin.Api.Core;

// Reuses the JSON payload of ty_works/ty_orders/etc. Unknown Go fields survive round trips.
public static class Doc
{
    public static string Str(JsonNode? obj, string key, string fallback = "") =>
        obj is JsonObject map && map.TryGetPropertyValue(key, out var n) && n != null ? n.ToString() : fallback;
    public static int Int(JsonNode? obj, string key, int fallback = 0) =>
        int.TryParse(Str(obj, key), out var v) ? v : fallback;
    public static bool Bool(JsonNode? obj, string key) => bool.TryParse(Str(obj, key), out var b) && b;
    public static JsonArray Array(JsonNode? obj, string key) =>
        obj is JsonObject map && map.TryGetPropertyValue(key, out var n) && n is JsonArray arr ? arr : new JsonArray();
    public static JsonObject Copy(JsonObject obj) => (JsonObject)obj.DeepClone();
    public static string NewId() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
    public static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
    public static JsonObject Parse(string s) => JsonNode.Parse(s) as JsonObject ?? new JsonObject();
}
public sealed class Snapshot
{
    public Dictionary<string, JsonObject> Works { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonObject> Orders { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonObject> Addresses { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonObject> Users { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonObject> Admins { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonObject> Audit { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonObject> AIProviders { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonObject> AIPrompts { get; set; } = new(StringComparer.Ordinal);

    public IEnumerable<(string Table, Dictionary<string, JsonObject> Values)> Sections() =>
        new (string, Dictionary<string, JsonObject>)[]
        {
            ("ty_works", Works), ("ty_orders", Orders), ("ty_addresses", Addresses),
            ("ty_users", Users), ("ty_admins", Admins), ("ty_audit", Audit),
            ("ty_ai_providers", AIProviders), ("ty_ai_prompts", AIPrompts)
        };
    public Snapshot DeepClone()
    {
        var result = new Snapshot();
        foreach (var (name, values) in Sections())
        {
            var dest = result.Sections().Single(section => section.Table == name).Values;
            foreach (var (id, doc) in values) dest[id] = Doc.Copy(doc);
        }
        return result;
    }
    public JsonObject ToJson()
    {
        var obj = new JsonObject();
        foreach (var (name, values) in Sections())
        {
            var key = name == "ty_ai_providers" ? "ai_providers" :
                      name == "ty_ai_prompts" ? "ai_prompts" : name["ty_".Length..];
            var sub = new JsonObject();
            foreach (var (id, value) in values) sub[id] = value.DeepClone();
            obj[key] = sub;
        }
        return obj;
    }
    public static Snapshot FromJson(JsonObject obj)
    {
        var result = new Snapshot();
        foreach (var (name, values) in result.Sections())
        {
            var key = name == "ty_ai_providers" ? "ai_providers" :
                      name == "ty_ai_prompts" ? "ai_prompts" : name["ty_".Length..];
            if (obj[key] is not JsonObject sub) continue;
            foreach (var (id, value) in sub)
                if (value is JsonObject entry) values[id] = Doc.Copy(entry);
        }
        return result;
    }
}
