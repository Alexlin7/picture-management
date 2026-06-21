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
}
