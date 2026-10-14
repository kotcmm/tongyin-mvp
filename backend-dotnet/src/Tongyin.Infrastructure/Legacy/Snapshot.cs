using System.Text.Json.Nodes;

namespace Tongyin.Api.Core;

// Reuses the JSON payload of ty_works/ty_orders/etc. Unknown Go fields survive round trips.

/// <summary>
/// Go 数据表快照，统一表示现存数据结构。
/// </summary>
public sealed class Snapshot
{
    /// <summary>
    /// 已保存的儿童画作品集合。
    /// </summary>
    public Dictionary<string, JsonObject> Works { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 客户订单集合。
    /// </summary>
    public Dictionary<string, JsonObject> Orders { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 客户收货地址集合。
    /// </summary>
    public Dictionary<string, JsonObject> Addresses { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 客户端用户集合。
    /// </summary>
    public Dictionary<string, JsonObject> Users { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 后台管理员集合。
    /// </summary>
    public Dictionary<string, JsonObject> Admins { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 操作审计记录集合。
    /// </summary>
    public Dictionary<string, JsonObject> Audit { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// AI（人工智能）服务商配置集合。
    /// </summary>
    public Dictionary<string, JsonObject> AIProviders { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// AI 图像和模型生成提示词集合。
    /// </summary>
    public Dictionary<string, JsonObject> AIPrompts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 按照原有数据库表结构列举快照中的数据集合。
    /// </summary>
    public IEnumerable<(string Table, Dictionary<string, JsonObject> Values)> Sections() =>
        new (string, Dictionary<string, JsonObject>)[]
        {
            ("ty_works", Works), ("ty_orders", Orders), ("ty_addresses", Addresses),
            ("ty_users", Users), ("ty_admins", Admins), ("ty_audit", Audit),
            ("ty_ai_providers", AIProviders), ("ty_ai_prompts", AIPrompts)
        };
    /// <summary>
    /// 深度复制整个数据快照。
    /// </summary>
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
    /// <summary>
    /// 序列化为旧版 JSON 数据结构。
    /// </summary>
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
    /// <summary>
    /// 从旧版 JSON 数据结构恢复快照。
    /// </summary>
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
