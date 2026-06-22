using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Scanner;

public sealed record TagListItem(long Id, string Name, string Kind, int Count);

// 標籤庫操作:正規化、不分大小寫 upsert、列表含使用數、改名/合併/刪除。
// 集中在此(而非散在 endpoint),供 manual 加標籤、autocomplete、標籤庫管理頁共用。
public sealed partial class TagService(PmDbContext db)
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    // 正規化:trim + 收合內部連續空白為單一空白。
    // 刻意「不」強制小寫/底線(保留顯示拼寫如 VSpo!、角色名);去重靠下面的 CI 比對。
    public static string Normalize(string name)
        => WhitespaceRun().Replace((name ?? string.Empty).Trim(), " ");

    // 以名稱「不分大小寫」找既有 tag;沒有才建(用正規化後拼寫)。→ blue/Blue 不再變兩個。
    public async Task<Tag> UpsertByNameAsync(string rawName, string kind, CancellationToken ct = default)
    {
        var name = Normalize(rawName);
        var lower = name.ToLowerInvariant();
        var existing = await db.Tags.FirstOrDefaultAsync(t => t.Name.ToLower() == lower, ct);
        if (existing is not null) return existing;

        var tag = new Tag { Name = name, Kind = kind };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
        return tag;
    }

    // 列出 tag + 使用數;q 不分大小寫 contains 過濾;依使用數 desc、名稱 asc;限 limit 筆。
    public async Task<IReadOnlyList<TagListItem>> ListAsync(string? q, int limit, CancellationToken ct = default)
    {
        var query = db.Tags.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var lower = q.Trim().ToLowerInvariant();
            query = query.Where(t => t.Name.ToLower().Contains(lower));
        }
        // SQL 做 WHERE + 投影(含 count 相關子查詢);排序/take 在記憶體
        // (SQLite 無法 ORDER BY 相關子查詢結果;tag 量級不大,可接受)。
        var items = await query
            .Select(t => new TagListItem(t.Id, t.Name, t.Kind, db.PhotoTags.Count(pt => pt.TagId == t.Id)))
            .ToListAsync(ct);
        return items
            .OrderByDescending(i => i.Count)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    // 刪除 tag(連帶 photo_tag / tag_relation 由 FK cascade 處理)。回是否存在。
    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var tag = await db.Tags.FindAsync([id], ct);
        if (tag is null) return false;
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // 合併 from → to:把 from 的 photo_tag 轉到 to(to 已有則丟棄),刪掉 from。回是否成功。
    public async Task<bool> MergeAsync(long fromId, long toId, CancellationToken ct = default)
    {
        if (fromId == toId) return false;
        var from = await db.Tags.FindAsync([fromId], ct);
        var to = await db.Tags.FindAsync([toId], ct);
        if (from is null || to is null) return false;

        var fromLinks = await db.PhotoTags.Where(pt => pt.TagId == fromId).ToListAsync(ct);
        var toPhotoIds = (await db.PhotoTags.Where(pt => pt.TagId == toId).Select(pt => pt.PhotoId).ToListAsync(ct))
            .ToHashSet();

        foreach (var link in fromLinks)
        {
            if (toPhotoIds.Contains(link.PhotoId)) continue;   // to 已有此 photo → 丟棄 from 的關聯
            db.PhotoTags.Add(new PhotoTag
            {
                PhotoId = link.PhotoId, TagId = toId, Source = link.Source, Confidence = link.Confidence
            });
        }
        db.Tags.Remove(from);   // cascade 刪掉 from 的 photo_tag
        await db.SaveChangesAsync(ct);
        return true;
    }

    // 改名:正規化;若有「另一個」tag 同名(CI)→ 合併到它;否則純改名。回 (found, merged)。
    public async Task<(bool Found, bool Merged)> RenameAsync(long id, string newName, CancellationToken ct = default)
    {
        var tag = await db.Tags.FindAsync([id], ct);
        if (tag is null) return (false, false);

        var name = Normalize(newName);
        var lower = name.ToLowerInvariant();
        var clash = await db.Tags.FirstOrDefaultAsync(t => t.Id != id && t.Name.ToLower() == lower, ct);
        if (clash is not null)
        {
            await MergeAsync(id, clash.Id, ct);
            return (true, true);
        }
        tag.Name = name;
        await db.SaveChangesAsync(ct);
        return (true, false);
    }
}
