using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class TagServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-tagsvc-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public TagServiceTests()
    {
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    private async Task<long> SeedPhoto(char hashChar)
    {
        await using var ctx = NewContext();
        var p = new Photo { FileHash = new string(hashChar, 64), FileSize = 1 };
        ctx.Photos.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    [Theory]
    [InlineData("  blue  ", "blue")]
    [InlineData("long   hair", "long hair")]
    [InlineData("VSpo!", "VSpo!")]
    public void Normalize_trims_and_collapses_whitespace(string raw, string expected)
        => Assert.Equal(expected, TagService.Normalize(raw));

    [Fact]
    public async Task Upsert_is_case_insensitive_keeps_first_spelling()
    {
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        var a = await svc.UpsertByNameAsync("Blue", "manual");
        var b = await svc.UpsertByNameAsync("blue", "manual");    // 同字不同大小寫
        var c = await svc.UpsertByNameAsync(" BLUE ", "manual");  // 空白 + 大小寫

        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Id, c.Id);
        Assert.Equal("Blue", a.Name);                 // 保留首見拼寫
        Assert.Equal(1, await ctx.Tags.CountAsync());  // 只有一個 tag
    }

    [Fact]
    public async Task Upsert_dedupes_non_ascii_case_insensitively()
    {
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        // 西里爾「Ёжик」大小寫變體:SQLite 內建 lower() 只折 ASCII,
        // 需靠 name_ci(C# ToLowerInvariant)才會去重(否則變兩筆)。
        var a = await svc.UpsertByNameAsync("Ёжик", "general");
        var b = await svc.UpsertByNameAsync("ёжик", "general");

        Assert.Equal(a.Id, b.Id);
        Assert.Equal("Ёжик", a.Name);                  // 保留首見拼寫
        Assert.Equal(1, await ctx.Tags.CountAsync());  // 只有一個 tag
    }

    [Fact]
    public async Task Upsert_upgrades_kind_semantically_but_never_downgrades()
    {
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        // 先手動加(manual=未分類佔位),再被 wd14 標為 character → 升級。
        var t1 = await svc.UpsertByNameAsync("reimu", "manual");
        var t2 = await svc.UpsertByNameAsync("reimu", "character");
        Assert.Equal(t1.Id, t2.Id);
        Assert.Equal("character", (await ctx.Tags.FindAsync(t1.Id))!.Kind);

        // 之後較弱的 kind(general / manual)不得把 character 降級回去。
        await svc.UpsertByNameAsync("reimu", "general");
        await svc.UpsertByNameAsync("reimu", "manual");
        Assert.Equal("character", (await ctx.Tags.FindAsync(t1.Id))!.Kind);
    }

    [Fact]
    public async Task Upsert_rejects_blank_name()
    {
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertByNameAsync("   ", "manual"));
        Assert.Equal(0, await ctx.Tags.CountAsync());
    }

    [Fact]
    public async Task Update_sets_kind_directly_without_semantic_rules()
    {
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        var t = await svc.UpsertByNameAsync("reimu", "character");
        // 標籤庫「明示」改 kind:character→general 即使是降級也照辦(不走 upsert 的 KindRank)。
        var (found, merged) = await svc.UpdateAsync(t.Id, null, "general");
        Assert.True(found);
        Assert.False(merged);
        Assert.Equal("general", (await ctx.Tags.FindAsync(t.Id))!.Kind);
    }

    [Fact]
    public async Task Update_changes_name_and_kind_together()
    {
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        var t = await svc.UpsertByNameAsync("oldname", "manual");
        var (found, merged) = await svc.UpdateAsync(t.Id, "NewName", "copyright");
        Assert.True(found);
        Assert.False(merged);
        var u = await ctx.Tags.FindAsync(t.Id);
        Assert.Equal("NewName", u!.Name);     // 改名 + 保留拼寫
        Assert.Equal("copyright", u.Kind);    // 同時改 kind
    }

    [Fact]
    public async Task Update_rename_to_existing_still_merges()
    {
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        var src = await svc.UpsertByNameAsync("blu", "general");
        await svc.UpsertByNameAsync("blue", "general");
        var (found, merged) = await svc.UpdateAsync(src.Id, "Blue", null);   // 撞既有(CI)→ 合併
        Assert.True(found);
        Assert.True(merged);
        Assert.Null(await ctx.Tags.FindAsync(src.Id));
    }

    [Fact]
    public async Task Rename_to_blank_is_rejected()
    {
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        var t = await svc.UpsertByNameAsync("keep", "manual");
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RenameAsync(t.Id, "   "));
        Assert.Equal("keep", (await ctx.Tags.FindAsync(t.Id))!.Name);   // 未被改成空
    }

    [Fact]
    public async Task List_returns_usage_counts_and_ci_filter()
    {
        var photoId = await SeedPhoto('a');
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        var blue = await svc.UpsertByNameAsync("blue", "general");
        await svc.UpsertByNameAsync("red", "general");
        ctx.PhotoTags.Add(new PhotoTag { PhotoId = photoId, TagId = blue.Id, Source = "manual" });
        await ctx.SaveChangesAsync();

        var all = await svc.ListAsync(null, 50);
        Assert.Equal(2, all.Count);
        Assert.Equal("blue", all[0].Name);    // count desc:blue(1) 在 red(0) 前
        Assert.Equal(1, all[0].Count);
        Assert.Equal(0, all.Single(t => t.Name == "red").Count);

        var filtered = await svc.ListAsync("BL", 50);   // 不分大小寫過濾
        Assert.Single(filtered);
        Assert.Equal("blue", filtered[0].Name);
    }

    [Fact]
    public async Task Merge_repoints_links_and_deletes_source()
    {
        var p1 = await SeedPhoto('a');
        var p2 = await SeedPhoto('b');
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        var from = await svc.UpsertByNameAsync("1girl", "general");
        var to = await svc.UpsertByNameAsync("1girls", "general");
        ctx.PhotoTags.Add(new PhotoTag { PhotoId = p1, TagId = from.Id, Source = "wd14" });
        ctx.PhotoTags.Add(new PhotoTag { PhotoId = p2, TagId = from.Id, Source = "wd14" });
        ctx.PhotoTags.Add(new PhotoTag { PhotoId = p2, TagId = to.Id, Source = "manual" });   // p2 已掛 to
        await ctx.SaveChangesAsync();

        Assert.True(await svc.MergeAsync(from.Id, to.Id));

        await using var v = NewContext();
        Assert.Null(await v.Tags.FindAsync(from.Id));                              // from 已刪
        Assert.Equal(2, await v.PhotoTags.CountAsync(pt => pt.TagId == to.Id));    // p1(轉移)+ p2(原有,不重複)
    }

    [Fact]
    public async Task Rename_to_existing_name_merges()
    {
        var p1 = await SeedPhoto('a');
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        var src = await svc.UpsertByNameAsync("blu", "general");
        var dst = await svc.UpsertByNameAsync("blue", "general");
        ctx.PhotoTags.Add(new PhotoTag { PhotoId = p1, TagId = src.Id, Source = "manual" });
        await ctx.SaveChangesAsync();

        var (found, merged) = await svc.RenameAsync(src.Id, "Blue");   // 撞既有(CI)→ 合併
        Assert.True(found);
        Assert.True(merged);

        await using var v = NewContext();
        Assert.Null(await v.Tags.FindAsync(src.Id));
        Assert.Equal(1, await v.PhotoTags.CountAsync(pt => pt.TagId == dst.Id));
    }

    [Fact]
    public async Task Rename_plain_then_delete()
    {
        await using var ctx = NewContext();
        var svc = new TagService(ctx);
        var t = await svc.UpsertByNameAsync("oldname", "general");

        var (found, merged) = await svc.RenameAsync(t.Id, "newname");
        Assert.True(found);
        Assert.False(merged);
        Assert.Equal("newname", (await ctx.Tags.FindAsync(t.Id))!.Name);

        Assert.True(await svc.DeleteAsync(t.Id));
        Assert.False(await svc.DeleteAsync(t.Id));   // 已不存在
    }
}
