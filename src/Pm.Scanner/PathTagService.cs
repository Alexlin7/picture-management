using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Scanner;

public sealed class PathTagService(PmDbContext db)
{
    /// <summary>此 root 下所有「目錄段」(排除檔名),扣掉已有規則者,附次數/範例/建議。</summary>
    public async Task<IReadOnlyList<PendingSegment>> GetPendingSegmentsAsync(
        long rootId, CancellationToken ct = default)
    {
        var relPaths = await db.PhotoLocations
            .Where(l => l.LibraryRootId == rootId)
            .Select(l => l.RelPath)
            .ToListAsync(ct);

        var ruled = (await db.PathTagRules
            .Where(r => r.LibraryRootId == rootId || r.LibraryRootId == null)
            .Select(r => r.Segment)
            .ToListAsync(ct)).ToHashSet();

        var count = new Dictionary<string, int>();
        var sample = new Dictionary<string, string>();
        foreach (var rel in relPaths)
        {
            var parts = rel.Split('/');
            for (int i = 0; i < parts.Length - 1; i++)   // 最後一段是檔名,跳過
            {
                var seg = parts[i];
                if (seg.Length == 0 || ruled.Contains(seg)) continue;
                count[seg] = count.GetValueOrDefault(seg) + 1;
                sample.TryAdd(seg, rel);
            }
        }

        return count
            .Select(kv => new PendingSegment(kv.Key, kv.Value, sample[kv.Key], PathTagDefaults.Suggest(kv.Key)))
            .OrderByDescending(p => p.Count)
            .ToList();
    }

    public async Task ApplyRuleAsync(
        long? rootId, string segment, string action, string? tagName, CancellationToken ct = default)
    {
        long? tagId = null;
        if (action is "map_to_tag" or "meta_year")
        {
            var name = action == "meta_year" ? segment : (tagName ?? segment);
            var kind = action == "meta_year" ? "meta" : "path";
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is null)
            {
                tag = new Tag { Name = name, Kind = kind };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(ct);
            }
            tagId = tag.Id;
        }

        var rule = await db.PathTagRules
            .FirstOrDefaultAsync(r => r.LibraryRootId == rootId && r.Segment == segment, ct);
        if (rule is null)
            db.PathTagRules.Add(new PathTagRule { LibraryRootId = rootId, Segment = segment, Action = action, TagId = tagId });
        else { rule.Action = action; rule.TagId = tagId; }
        await db.SaveChangesAsync(ct);

        if (tagId is not null)
            await ApplySegmentTagAsync(rootId, segment, tagId.Value, ct);
    }

    private async Task ApplySegmentTagAsync(long? rootId, string segment, long tagId, CancellationToken ct)
    {
        var pat = segment;
        var photoIds = await db.PhotoLocations
            .Where(l => (rootId == null || l.LibraryRootId == rootId)
                        && (l.RelPath.StartsWith(pat + "/") || l.RelPath.Contains("/" + pat + "/")))
            .Select(l => l.PhotoId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var pid in photoIds)
            if (!await db.PhotoTags.AnyAsync(pt => pt.PhotoId == pid && pt.TagId == tagId, ct))
                db.PhotoTags.Add(new PhotoTag { PhotoId = pid, TagId = tagId, Source = "path" });

        await db.SaveChangesAsync(ct);
    }
}
