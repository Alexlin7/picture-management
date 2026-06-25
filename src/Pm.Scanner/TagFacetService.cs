using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Scanner;

public sealed record FacetNode(string Name, string Kind, int Count, bool Multi, List<FacetNode>? Children);

public sealed record FacetTree(
    List<FacetNode> Tree,
    List<FacetNode> Rootless,
    List<(string Name, int Count)> General,
    List<(string Name, int Count)> Meta);

/// <summary>
/// 側欄 facet 樹:由 tag_relation 組階層,count 以「直接擁有該 tag 的 present photo 數」計
/// (不展開後代,避免十萬量級下每節點都跑 recursive CTE)。
/// </summary>
public sealed class TagFacetService(PmDbContext db)
{
    private const int TopN = 30;

    public async Task<FacetTree> BuildAsync(CancellationToken ct = default)
    {
        var tags = await db.Tags
            .Select(t => new { t.Id, t.Name, t.Kind })
            .ToListAsync(ct);

        // 直接擁有該 tag 的 present photo 數(只算有 present location 的 photo)
        var counts = (await db.PhotoTags
            .Where(pt => db.PhotoLocations.Any(l => l.PhotoId == pt.PhotoId && l.Status == "present"))
            .GroupBy(pt => pt.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Select(x => x.PhotoId).Distinct().Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.TagId, x => x.Count);

        var edges = await db.TagRelations
            .Select(r => new { r.ParentTagId, r.ChildTagId })
            .ToListAsync(ct);

        var childrenOf = edges
            .GroupBy(e => e.ParentTagId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ChildTagId).ToList());

        var parentEdgeCount = edges
            .GroupBy(e => e.ChildTagId)
            .ToDictionary(g => g.Key, g => g.Count());

        var byId = tags.ToDictionary(t => t.Id);

        int CountFor(long id) => counts.GetValueOrDefault(id);
        bool MultiFor(long id) => parentEdgeCount.GetValueOrDefault(id) >= 2;

        FacetNode Build(long id, HashSet<long> path)
        {
            var t = byId[id];
            List<FacetNode>? kids = null;
            if (childrenOf.TryGetValue(id, out var cids))
            {
                kids = new List<FacetNode>();
                foreach (var cid in cids)
                {
                    if (path.Contains(cid)) continue;   // 防環
                    path.Add(cid);
                    kids.Add(Build(cid, path));
                    path.Remove(cid);
                }
                if (kids.Count == 0) kids = null;
            }
            var count = CountFor(id);
            if (t.Kind == "copyright" && kids is not null)
                count = kids.Sum(k => k.Count);   // copyright 直接無圖,以子角色 count 聚合(facet 顯示用近似)
            return new FacetNode(t.Name, t.Kind, count, MultiFor(id), kids);
        }

        // root = 沒有任何 parent 邊的 tag,但有子(會展開成樹);
        // rootless = 沒有任何 parent 邊「且」沒有子的孤立 tag。
        var hasParent = parentEdgeCount.Keys.ToHashSet();
        var hasChild = childrenOf.Keys.ToHashSet();

        var tree = new List<FacetNode>();
        var rootless = new List<FacetNode>();
        foreach (var t in tags)
        {
            if (t.Kind != "copyright" && t.Kind != "character") continue;   // 樹只收作品/角色;general/meta 各有專屬區
            if (hasParent.Contains(t.Id)) continue;   // 非頂層
            if (hasChild.Contains(t.Id))
                tree.Add(Build(t.Id, new HashSet<long> { t.Id }));
            else
                rootless.Add(new FacetNode(t.Name, t.Kind, CountFor(t.Id), MultiFor(t.Id), null));
        }

        List<(string, int)> Top(string kind) => tags
            .Where(t => t.Kind == kind)
            .Select(t => (t.Name, CountFor(t.Id)))
            .OrderByDescending(x => x.Item2)
            .ThenBy(x => x.Name)
            .Take(TopN)
            .ToList();

        tree = tree.OrderByDescending(n => n.Count).ThenBy(n => n.Name).ToList();
        rootless = rootless.OrderByDescending(n => n.Count).ThenBy(n => n.Name).ToList();

        return new FacetTree(tree, rootless, Top("general"), Top("meta"));
    }
}
