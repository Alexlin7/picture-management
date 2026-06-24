using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Scanner;

public sealed class PhotoQueryService(PmDbContext db, TagClosureService closure)
{
    public async Task<PhotoPage> SearchAsync(
        IEnumerable<string> all, IEnumerable<string> none,
        long? afterId, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);

        // 每個 include tag → 後代閉包群組;照片需命中所有群組。
        var includeGroups = new List<List<long>>();
        foreach (var name in all.Distinct())
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is null) return new PhotoPage(Array.Empty<PhotoListItem>(), null);  // 未知 tag → 無結果
            includeGroups.Add(await closure.DescendantsAsync(tag.Id, ct));
        }

        var excludeIds = new List<long>();
        foreach (var name in none.Distinct())
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is not null) excludeIds.AddRange(await closure.DescendantsAsync(tag.Id, ct));
        }

        var q = db.Photos.Where(p => p.Locations.Any(l => l.Status == "present"));
        foreach (var group in includeGroups)
            q = q.Where(p => p.Tags.Any(t => group.Contains(t.TagId)));
        if (excludeIds.Count > 0)
            q = q.Where(p => !p.Tags.Any(t => excludeIds.Contains(t.TagId)));
        if (afterId is not null)
            q = q.Where(p => p.Id < afterId);

        var rows = await q.OrderByDescending(p => p.Id).Take(pageSize + 1)
            .Select(p => new PhotoListItem(p.Id, p.FileHash, p.Width, p.Height, p.Mime))
            .ToListAsync(ct);

        long? next = rows.Count > pageSize ? rows[pageSize - 1].Id : null;
        if (rows.Count > pageSize) rows.RemoveAt(rows.Count - 1);
        return new PhotoPage(rows, next);
    }

    public async Task<long> CountAsync(
        IEnumerable<string> all, IEnumerable<string> none,
        CancellationToken ct = default)
    {
        var includeGroups = new List<List<long>>();
        foreach (var name in all.Distinct())
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is null) return 0;   // 未知 tag → 無結果
            includeGroups.Add(await closure.DescendantsAsync(tag.Id, ct));
        }

        var excludeIds = new List<long>();
        foreach (var name in none.Distinct())
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is not null) excludeIds.AddRange(await closure.DescendantsAsync(tag.Id, ct));
        }

        var q = db.Photos.Where(p => p.Locations.Any(l => l.Status == "present"));
        foreach (var group in includeGroups)
            q = q.Where(p => p.Tags.Any(t => group.Contains(t.TagId)));
        if (excludeIds.Count > 0)
            q = q.Where(p => !p.Tags.Any(t => excludeIds.Contains(t.TagId)));

        return await q.LongCountAsync(ct);
    }
}
