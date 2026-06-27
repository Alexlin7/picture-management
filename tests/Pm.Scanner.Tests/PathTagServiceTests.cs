using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class PathTagServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-pathtag-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public PathTagServiceTests()
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

    private async Task<long> SeedLocations(params string[] relPaths)
    {
        await using var ctx = NewContext();
        var root = new LibraryRoot { Name = "t", AbsPath = @"D:\x" };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        int i = 0;
        foreach (var rel in relPaths)
        {
            var photo = new Photo { FileHash = new string((char)('a' + i++), 64), FileSize = 1 };
            photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = rel });
            ctx.Photos.Add(photo);
        }
        await ctx.SaveChangesAsync();
        return root.Id;
    }

    [Fact]
    public async Task Lists_directory_segments_with_counts_and_suggestions()
    {
        var rootId = await SeedLocations("vspo/a.png", "vspo/b.png", "2434/vspo/c.png", "我不知道/d.png");

        await using var ctx = NewContext();
        var pending = await new PathTagService(ctx).GetPendingSegmentsAsync(rootId);

        var vspo = pending.Single(p => p.Segment == "vspo");
        Assert.Equal(3, vspo.Count);
        Assert.Equal("map_to_tag", vspo.SuggestedAction);
        Assert.Equal("ignore", pending.Single(p => p.Segment == "我不知道").SuggestedAction);
        Assert.Equal("map_to_tag", pending.Single(p => p.Segment == "2434").SuggestedAction);
        Assert.DoesNotContain(pending, p => p.Segment.EndsWith(".png"));   // 檔名不算段
    }

    [Fact]
    public async Task Apply_map_to_tag_creates_tag_and_tags_photos()
    {
        var rootId = await SeedLocations("vspo/a.png", "vspo/sub/b.png", "other/c.png");

        await using (var ctx = NewContext())
            await new PathTagService(ctx).ApplyRuleAsync(rootId, "vspo", "map_to_tag", tagName: "vspo");

        await using var verify = NewContext();
        var tag = await verify.Tags.SingleAsync(t => t.Name == "vspo");
        Assert.Equal("path", tag.Kind);
        Assert.Equal(2, await verify.PhotoTags.CountAsync(pt => pt.TagId == tag.Id && pt.Source == "path"));
        Assert.False(await verify.PathTagRules
            .AnyAsync(r => r.Segment == "vspo" && r.Action != "map_to_tag"));
    }

    [Fact]
    public async Task Apply_ignore_records_rule_without_tag()
    {
        var rootId = await SeedLocations("我不知道/a.png");

        await using (var ctx = NewContext())
            await new PathTagService(ctx).ApplyRuleAsync(rootId, "我不知道", "ignore", null);

        await using var verify = NewContext();
        Assert.Equal("ignore", (await verify.PathTagRules.SingleAsync()).Action);
        Assert.Equal(0, await verify.PhotoTags.CountAsync());

        // 確認後不再列入待確認
        await using var ctx2 = NewContext();
        var pending = await new PathTagService(ctx2).GetPendingSegmentsAsync(rootId);
        Assert.DoesNotContain(pending, p => p.Segment == "我不知道");
    }

    [Fact]
    public async Task Apply_meta_year_creates_meta_kind_tag()
    {
        var rootId = await SeedLocations("2024/a.png");

        await using (var ctx = NewContext())
            await new PathTagService(ctx).ApplyRuleAsync(rootId, "2024", "meta_year", null);

        await using var verify = NewContext();
        var tag = await verify.Tags.SingleAsync(t => t.Name == "2024");
        Assert.Equal("meta", tag.Kind);
        Assert.Equal(1, await verify.PhotoTags.CountAsync(pt => pt.TagId == tag.Id));
    }

    [Fact]
    public async Task Apply_raw_map_action_is_normalized_and_creates_tag()
    {
        // bug 修復:前端送 action='map'(非 'map_to_tag')也要正規化並建 tag。
        var rootId = await SeedLocations("vspo/a.png");

        await using (var ctx = NewContext())
            await new PathTagService(ctx).ApplyRuleAsync(rootId, "vspo", "map", tagName: "vspo");

        await using var verify = NewContext();
        var tag = await verify.Tags.SingleAsync(t => t.Name == "vspo");
        Assert.Equal(1, await verify.PhotoTags.CountAsync(pt => pt.TagId == tag.Id));
        Assert.Equal("map_to_tag", (await verify.PathTagRules.SingleAsync()).Action);
    }

    [Fact]
    public async Task Apply_map_to_tag_uses_caller_kind()
    {
        // bug 修復:前端選的分類(kind)要生效,不再寫死 path。
        var rootId = await SeedLocations("akira/a.png");

        await using (var ctx = NewContext())
            await new PathTagService(ctx).ApplyRuleAsync(rootId, "akira", "map", tagName: "akira", kind: "character");

        await using var verify = NewContext();
        Assert.Equal("character", (await verify.Tags.SingleAsync(t => t.Name == "akira")).Kind);
    }

    [Fact]
    public async Task Existing_rules_self_heal_old_map_rule_missing_tag()
    {
        // 歷史 bug:action='map' 規則 TagId=null(沒建 tag)。ApplyExistingRulesAsync 應補建並套用。
        var rootId = await SeedLocations("vspo/a.png");
        await using (var ctx = NewContext())
        {
            ctx.PathTagRules.Add(new PathTagRule { LibraryRootId = rootId, Segment = "vspo", Action = "map", TagId = null });
            await ctx.SaveChangesAsync();
        }

        int applied;
        await using (var ctx = NewContext())
            applied = await new PathTagService(ctx).ApplyExistingRulesAsync(rootId);

        Assert.Equal(1, applied);
        await using var verify = NewContext();
        var rule = await verify.PathTagRules.SingleAsync();
        Assert.Equal("map_to_tag", rule.Action);   // 動作已正規化
        Assert.NotNull(rule.TagId);                 // TagId 已回填
        var tag = await verify.Tags.SingleAsync(t => t.Name == "vspo");
        Assert.Equal(1, await verify.PhotoTags.CountAsync(pt => pt.TagId == tag.Id));
    }

    [Fact]
    public async Task Existing_rules_apply_to_newly_added_photos()
    {
        var rootId = await SeedLocations("vspo/a.png");
        await using (var ctx = NewContext())
            await new PathTagService(ctx).ApplyRuleAsync(rootId, "vspo", "map_to_tag", "vspo");

        // 之後又進來一張同段新照片
        await using (var ctx = NewContext())
        {
            var root = await ctx.LibraryRoots.FindAsync(rootId);
            var photo = new Photo { FileHash = new string('z', 64), FileSize = 1 };
            photo.Locations.Add(new PhotoLocation { LibraryRoot = root!, RelPath = "vspo/new.png" });
            ctx.Photos.Add(photo);
            await ctx.SaveChangesAsync();
        }

        int applied;
        await using (var ctx = NewContext())
            applied = await new PathTagService(ctx).ApplyExistingRulesAsync(rootId);

        Assert.Equal(1, applied);
        await using var verify = NewContext();
        var tag = await verify.Tags.SingleAsync(t => t.Name == "vspo");
        Assert.Equal(2, await verify.PhotoTags.CountAsync(pt => pt.TagId == tag.Id));   // 舊 + 新
    }
}
