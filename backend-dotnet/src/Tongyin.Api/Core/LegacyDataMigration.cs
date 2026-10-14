using System.Text.Json.Nodes;

namespace Tongyin.Api.Core;

/// <summary>
/// 根据 Go 旧数据恢复缺失客户记录与已经上传的三维模型状态；可重复执行。
/// </summary>
public static class LegacyDataMigration
{
    /// <summary>
    /// 幂等修复 Go 历史用户与作品数据。
    /// </summary>
    public static Task<(int Users, int Models)> Apply(IStore store) =>
        store.Update(s =>
        {
            var users=0;
            var models=0;
            var owners=s.Works.Values.Select(w=>Doc.Str(w,"owner"))
                .Concat(s.Orders.Values.Select(o=>Doc.Str(o,"owner")))
                .Where(o=>o!="")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach(var owner in owners)
            {
                if(s.Users.ContainsKey(owner))continue;
                s.Users[owner]=new JsonObject
                {
                    ["id"]=Doc.NewId(),["owner"]=owner,["created_at"]=Doc.Now(),
                    ["disabled"]=false
                };
                users++;
            }
            foreach(var work in s.Works.Values)
            {
                if(Doc.Str(work,"status")!="model_pending" || Doc.Str(work,"model")=="")continue;
                work["status"]="model_ready";
                models++;
            }
            return (users,models);
        });
}
