using System.Text.Json.Nodes;

namespace Tongyin.Api.Core;

/// <summary>
/// Matches the two non-destructive startup repairs performed by Go main():
/// users owned by existing works/orders and legacy GLBs awaiting static previews.
/// Safe to re-run; do not create duplicate owners or mutate current jobs.
/// </summary>
public static class LegacyDataMigration
{
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
