using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Scanner;

public sealed class PhotoQueryService(PmDbContext db, TagClosureService closure)
{
    // 資料夾範圍過濾:限縮到指定 root 的子樹(StartsWith prefix+"/" 避免兄弟前綴誤中);rootId null = 不限縮。
    // SearchAsync 與 CountAsync 共用同一份,確保 count 與 page 永遠對齊。
    private static IQueryable<Photo> ApplyFolderScope(IQueryable<Photo> q, long? rootId, string? pathPrefix)
    {
        if (rootId is null) return q;
        var prefix = pathPrefix ?? "";
        return q.Where(p => p.Locations.Any(l =>
            l.Status == "present"
            && l.LibraryRootId == rootId
            && (prefix == "" || l.RelPath.StartsWith(prefix + "/"))));
    }

    public async Task<PhotoPage> SearchAsync(
        IEnumerable<string> all, IEnumerable<string> none,
        long? afterId, int pageSize,
        long? rootId = null, string? pathPrefix = null,
        CancellationToken ct = default)
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
        q = ApplyFolderScope(q, rootId, pathPrefix);
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
        long? rootId = null, string? pathPrefix = null,
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
        q = ApplyFolderScope(q, rootId, pathPrefix);
        foreach (var group in includeGroups)
            q = q.Where(p => p.Tags.Any(t => group.Contains(t.TagId)));
        if (excludeIds.Count > 0)
            q = q.Where(p => !p.Tags.Any(t => excludeIds.Contains(t.TagId)));

        return await q.LongCountAsync(ct);
    }

    // 回傳符合布林查詢的全部 photoId(不分頁),供 TaggingScheduler 的 Query scope 用。
    // 與 SearchAsync/CountAsync 共用同一 closure + ApplyFolderScope 邏輯;
    // 空 all/none 等同「全部 present」(語意同 SearchAsync 無 token);未知 include tag → 空清單。
    public async Task<List<long>> GetAllPhotoIdsAsync(
        IEnumerable<string> all, IEnumerable<string> none,
        long? rootId = null, string? pathPrefix = null,
        CancellationToken ct = default)
    {
        var includeGroups = new List<List<long>>();
        foreach (var name in all.Distinct())
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is null) return new List<long>();   // 未知 tag → 無結果
            includeGroups.Add(await closure.DescendantsAsync(tag.Id, ct));
        }

        var excludeIds = new List<long>();
        foreach (var name in none.Distinct())
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is not null) excludeIds.AddRange(await closure.DescendantsAsync(tag.Id, ct));
        }

        var q = db.Photos.Where(p => p.Locations.Any(l => l.Status == "present"));
        q = ApplyFolderScope(q, rootId, pathPrefix);
        foreach (var group in includeGroups)
            q = q.Where(p => p.Tags.Any(t => group.Contains(t.TagId)));
        if (excludeIds.Count > 0)
            q = q.Where(p => !p.Tags.Any(t => excludeIds.Contains(t.TagId)));

        return await q.Select(p => p.Id).ToListAsync(ct);
    }
}
