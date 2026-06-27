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

    // 前端/舊資料動作詞彙正規化:map→map_to_tag、year→meta_year;其餘原樣。
    // 歷史上前端送 map/year 但此處只認 map_to_tag/meta_year,導致 map 規則靜默不建 tag(bug)。
    public static string NormalizeAction(string action) => action switch
    {
        "map" => "map_to_tag",
        "year" => "meta_year",
        _ => action,
    };

    public async Task ApplyRuleAsync(
        long? rootId, string segment, string action, string? tagName, string? kind = null, CancellationToken ct = default)
    {
        action = NormalizeAction(action);
        long? tagId = null;
        if (action is "map_to_tag" or "meta_year")
        {
            var name = action == "meta_year" ? segment : (tagName ?? segment);
            // meta_year 一律 meta;map_to_tag 用前端選的分類(cat),未給退 path。
            var tagKind = action == "meta_year" ? "meta" : (kind ?? "path");
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is null)
            {
                tag = new Tag { Name = name, Kind = tagKind };
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

    public async Task<int> ApplyExistingRulesAsync(long rootId, CancellationToken ct = default)
    {
        var rules = await db.PathTagRules
            .Where(r => r.LibraryRootId == rootId || r.LibraryRootId == null)
            .ToListAsync(ct);

        var applied = 0;
        foreach (var r in rules)
        {
            var tagId = r.TagId;
            if (tagId is null)
            {
                // 自我修復:歷史 bug 留下的 map_to_tag/meta_year 規則漏建 tag(TagId=null)。
                // 這些段已被排除在待確認外、無法重新確認 → 在此補建 tag 並回填 TagId。
                var action = NormalizeAction(r.Action);
                if (action is not ("map_to_tag" or "meta_year")) continue;   // ignore 等無 tag,略過
                var kind = action == "meta_year" ? "meta" : "path";          // 舊規則未存 cat,退預設
                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == r.Segment, ct);
                if (tag is null)
                {
                    tag = new Tag { Name = r.Segment, Kind = kind };
                    db.Tags.Add(tag);
                    await db.SaveChangesAsync(ct);
                }
                r.Action = action;   // 同時正規化動作詞彙
                r.TagId = tag.Id;
                await db.SaveChangesAsync(ct);
                tagId = tag.Id;
            }
            await ApplySegmentTagAsync(r.LibraryRootId, r.Segment, tagId.Value, ct);
            applied++;
        }

        return applied;
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
