using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Scanner;

// 從 WD14 character tag 拆作品:upsert copyright tag + 冪等寫 parent(copyright)→child(character) 邊。
// canonical 不動;衍生 copyright 以 Kind=copyright 識別(schema 無 source 欄)。供 TaggingWorker 即時 + backfill 共用。
public sealed class CopyrightAxisService(PmDbContext db, TagService tags, TagClosureService closure)
{
    // 回是否新增了邊(供 backfill 統計)。非 character / 無作品 / 已存在 / 成環 → false。
    public async Task<bool> SeedFromCharacterAsync(Tag characterTag, CancellationToken ct = default)
    {
        if (characterTag.Kind != "character") return false;
        var work = CopyrightAxis.ParseWork(characterTag.Name);
        if (work is null) return false;

        var copyright = await tags.UpsertByNameAsync(work, "copyright", ct);
        if (copyright.Id == characterTag.Id) return false;   // 防自我

        var exists = await db.TagRelations.AnyAsync(
            r => r.ParentTagId == copyright.Id && r.ChildTagId == characterTag.Id, ct);
        if (exists) return false;

        // 防環:copyright 已是 character 的後代 → 加 copyright→character 會成環,跳過。
        var childDescendants = await closure.DescendantsAsync(characterTag.Id, ct);
        if (childDescendants.Contains(copyright.Id)) return false;

        db.TagRelations.Add(new TagRelation { ParentTagId = copyright.Id, ChildTagId = characterTag.Id });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
