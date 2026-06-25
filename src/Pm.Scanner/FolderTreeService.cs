using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Scanner;

/// <summary>資料夾樹節點:名稱、累積相對路徑前綴(root 為 "")、遞迴 distinct present photo 數、子資料夾。</summary>
public sealed record FolderNode(string Name, string RelPath, int PhotoCount, List<FolderNode>? Children);

/// <summary>root 摘要:供 /browse 頂層並列各來源。</summary>
public sealed record FolderRoot(long Id, string Name, int PhotoCount);

/// <summary>
/// 即時資料夾樹:讀 photo_location.rel_path(只取 present)在記憶體建樹,後序算遞迴 distinct photo 數。
/// 不落表、不改 schema;反映硬碟當下結構,與 path→tag 維度正交。
/// </summary>
public sealed class FolderTreeService(PmDbContext db)
{
    public async Task<List<FolderRoot>> BuildRootsAsync(CancellationToken ct = default)
    {
        var roots = await db.LibraryRoots.Select(r => new { r.Id, r.Name }).ToListAsync(ct);
        var result = new List<FolderRoot>();
        foreach (var r in roots)
        {
            var count = await db.PhotoLocations
                .Where(l => l.LibraryRootId == r.Id && l.Status == "present")
                .Select(l => l.PhotoId).Distinct().CountAsync(ct);
            result.Add(new FolderRoot(r.Id, r.Name, count));
        }
        return result;
    }

    public async Task<FolderNode?> BuildTreeAsync(long rootId, CancellationToken ct = default)
    {
        var rootName = await db.LibraryRoots.Where(r => r.Id == rootId)
            .Select(r => r.Name).FirstOrDefaultAsync(ct);
        if (rootName is null) return null;

        var locs = await db.PhotoLocations
            .Where(l => l.LibraryRootId == rootId && l.Status == "present")
            .Select(l => new { l.RelPath, l.PhotoId })
            .ToListAsync(ct);

        var root = new MutableNode("", "");
        foreach (var loc in locs)
        {
            var parts = loc.RelPath.Split('/');
            var node = root;
            var prefix = "";
            for (var i = 0; i < parts.Length - 1; i++)   // 最後一段是檔名,跳過
            {
                var seg = parts[i];
                if (seg.Length == 0) continue;
                prefix = prefix.Length == 0 ? seg : prefix + "/" + seg;
                node = node.Child(seg, prefix);
            }
            node.PhotoIds.Add(loc.PhotoId);              // 掛到所在資料夾(可能就是 root)
        }

        return root.Fold(rootName).node;
    }

    /// <summary>建樹用的可變節點;Fold 後序合併子樹 photo id 取 distinct count。</summary>
    private sealed class MutableNode(string name, string relPath)
    {
        public string Name { get; } = name;
        public string RelPath { get; } = relPath;
        public Dictionary<string, MutableNode> Kids { get; } = new();
        public HashSet<long> PhotoIds { get; } = new();

        public MutableNode Child(string seg, string prefix)
        {
            if (!Kids.TryGetValue(seg, out var c))
            {
                c = new MutableNode(seg, prefix);
                Kids[seg] = c;
            }
            return c;
        }

        public (FolderNode node, HashSet<long> ids) Fold(string? displayName = null)
        {
            var all = new HashSet<long>(PhotoIds);
            List<FolderNode>? children = null;
            if (Kids.Count > 0)
            {
                children = new List<FolderNode>();
                foreach (var k in Kids.Values.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var (cn, cids) = k.Fold();
                    children.Add(cn);
                    all.UnionWith(cids);
                }
            }
            return (new FolderNode(displayName ?? Name, RelPath, all.Count, children), all);
        }
    }
}
