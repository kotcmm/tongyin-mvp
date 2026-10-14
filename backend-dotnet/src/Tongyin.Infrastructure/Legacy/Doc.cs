using System.Text.Json.Nodes;
using System.Text.Json;

namespace Tongyin.Api.Core;

// Reuses the JSON payload of ty_works/ty_orders/etc. Unknown Go fields survive round trips.

/// <summary>
/// Go 历史数据文档读写辅助类，只用于基础设施兼容。
/// </summary>
public static class Doc
{
    /// <summary>
    /// 读取字符串字段或返回默认值。
    /// </summary>
    public static string Str(JsonNode? obj, string key, string fallback = "") =>
        obj is JsonObject map && map.TryGetPropertyValue(key, out var n) && n != null ? n.ToString() : fallback;
    /// <summary>
    /// 读取整数字段或返回默认值。
    /// </summary>
    public static int Int(JsonNode? obj, string key, int fallback = 0) =>
        int.TryParse(Str(obj, key), out var v) ? v : fallback;
    /// <summary>
    /// 读取布尔字段。
    /// </summary>
    public static bool Bool(JsonNode? obj, string key) => bool.TryParse(Str(obj, key), out var b) && b;
    /// <summary>
    /// 读取数组字段或返回空数组。
    /// </summary>
    public static JsonArray Array(JsonNode? obj, string key) =>
        obj is JsonObject map && map.TryGetPropertyValue(key, out var n) && n is JsonArray arr ? arr : new JsonArray();
    /// <summary>
    /// 复制 JSON 数据对象。
    /// </summary>
    public static JsonObject Copy(JsonObject obj) => (JsonObject)obj.DeepClone();
    /// <summary>
    /// 生成 Go 兼容的唯一标识。
    /// </summary>
    public static string NewId() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
    /// <summary>
    /// 获取当前时间。
    /// </summary>
    public static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
    /// <summary>
    /// 解析 JSON 字符串。
    /// </summary>
    public static JsonObject Parse(string s) => JsonNode.Parse(s) as JsonObject ?? new JsonObject();
}
