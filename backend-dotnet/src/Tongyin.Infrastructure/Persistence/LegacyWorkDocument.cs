using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Domain.Works;

namespace Tongyin.Infrastructure.Persistence;

// Anti-corruption layer: Go's ty_works payload format never leaves Infrastructure.
public static class LegacyWorkDocument
{
    private static WorkStatus ParseStatus(string status) => status switch
    {
        "image_pending"=>WorkStatus.ImagePending, "image_ready"=>WorkStatus.ImageReady,
        "model_pending"=>WorkStatus.ModelPending, "model_ready"=>WorkStatus.ModelReady,
        "failed"=>WorkStatus.Failed, _=>WorkStatus.Unknown
    };

    private static string StatusValue(WorkStatus status) => status switch
    {
        WorkStatus.ImagePending=>"image_pending", WorkStatus.ImageReady=>"image_ready",
        WorkStatus.ModelPending=>"model_pending", WorkStatus.ModelReady=>"model_ready",
        WorkStatus.Failed=>"failed", _=>throw new InvalidOperationException("未知作品状态不能写入数据库")
    };

    /// <summary>
    /// 将 Go 数据文档映射为领域聚合。
    /// </summary>
    public static Work ToDomain(JsonObject raw) => Work.Restore(new WorkState(
        Doc.Str(raw,"id"),Doc.Str(raw,"owner"),Doc.Str(raw,"created_at"),
        Doc.Str(raw,"original"),ParseStatus(Doc.Str(raw,"status")),
        Doc.Int(raw,"image_attempts"),Doc.Str(raw,"generation_id"),
        Doc.Array(raw,"candidates").Select(x=>x?.ToString()??"").ToArray(),
        Doc.Int(raw,"selected",-1),
        Doc.Array(raw,"preview").Select(x=>x?.ToString()??"").ToArray(),
        Doc.Str(raw,"model"),Doc.Str(raw,"note")));

    // Unknown Go keys are preserved by cloning the stored JSON before patching fields.
    /// <summary>
    /// 将领域状态写回旧版数据格式，并保留未知字段。
    /// </summary>
    public static JsonObject ToDocument(WorkState state,JsonObject? previous=null)
    {
        var raw=previous is null ? new JsonObject() : Doc.Copy(previous);
        raw["id"]=state.Id;raw["owner"]=state.Owner;raw["created_at"]=state.CreatedAt;
        raw["original"]=state.OriginalAsset;raw["status"]=StatusValue(state.Status);
        raw["image_attempts"]=state.ImageAttempts;raw["selected"]=state.Selected;
        raw["candidates"]=new JsonArray(state.Candidates.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray());
        raw["preview"]=new JsonArray(state.Preview.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray());
        raw["model"]=state.ModelAsset;
        if(state.GenerationId!="" || raw.ContainsKey("generation_id"))
            raw["generation_id"]=state.GenerationId;
        if(state.Note!="" || raw.ContainsKey("note"))
            raw["note"]=state.Note;
        return raw;
    }
}
