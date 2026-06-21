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
}
