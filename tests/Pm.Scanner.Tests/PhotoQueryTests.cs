using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class PhotoQueryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-query-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public PhotoQueryTests()
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

    private PhotoQueryService Svc(PmDbContext ctx) => new(ctx, new TagClosureService(ctx));

    // 建一張有 present 位置、掛指定 tag 的照片
    private async Task<long> AddPhoto(PmDbContext ctx, LibraryRoot root, string hash, params long[] tagIds)
    {
        var photo = new Photo { FileHash = hash.PadRight(64, '0'), FileSize = 1 };
        photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = hash + ".png", Status = "present" });
        foreach (var tid in tagIds) photo.Tags.Add(new PhotoTag { TagId = tid, Source = "manual" });
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
        return photo.Id;
    }

    [Fact]
    public async Task And_intersection_and_implication_and_exclude()
    {
        long vspo, pekora, nsfw; long root;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "t", AbsPath = @"D:\x" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); root = r.Id;

            var t_vspo = new Tag { Name = "vspo", Kind = "copyright" };
            var t_pekora = new Tag { Name = "pekora", Kind = "character" };
            var t_nsfw = new Tag { Name = "nsfw", Kind = "meta" };
            ctx.Tags.AddRange(t_vspo, t_pekora, t_nsfw); await ctx.SaveChangesAsync();
            ctx.TagRelations.Add(new TagRelation { ParentTagId = t_vspo.Id, ChildTagId = t_pekora.Id });
            await ctx.SaveChangesAsync();
            vspo = t_vspo.Id; pekora = t_pekora.Id; nsfw = t_nsfw.Id;

            await AddPhoto(ctx, r, "p1", pekora);          // 只標子 pekora
            await AddPhoto(ctx, r, "p2", pekora, nsfw);    // pekora + nsfw
            await AddPhoto(ctx, r, "p3");                  // 無 tag
        }

        await using var ctx2 = NewContext();
        var svc = Svc(ctx2);

        // 搜上層 vspo → implication 命中 p1、p2(都只掛子 pekora)
        var byParent = await svc.SearchAsync(["vspo"], [], null, 200);
        Assert.Equal(2, byParent.Items.Count);

        // vspo 但排除 nsfw → 只剩 p1
        var excl = await svc.SearchAsync(["vspo"], ["nsfw"], null, 200);
        Assert.Single(excl.Items);

        // 未知 tag → 無結果
        var unknown = await svc.SearchAsync(["nonexistent"], [], null, 200);
        Assert.Empty(unknown.Items);
    }

    [Fact]
    public async Task Keyset_pagination_walks_all_pages()
    {
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "t", AbsPath = @"D:\x" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync();
            for (int i = 0; i < 5; i++) await AddPhoto(ctx, r, $"k{i}");
        }

        await using var ctx2 = NewContext();
        var svc = Svc(ctx2);

        var page1 = await svc.SearchAsync([], [], null, 2);
        Assert.Equal(2, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);

        var page2 = await svc.SearchAsync([], [], page1.NextCursor, 2);
        Assert.Equal(2, page2.Items.Count);

        var page3 = await svc.SearchAsync([], [], page2.NextCursor, 2);
        Assert.Single(page3.Items);
        Assert.Null(page3.NextCursor);   // 最後一頁
    }

    [Fact]
    public async Task Only_present_photos_returned()
    {
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "t", AbsPath = @"D:\x" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync();
            var photo = new Photo { FileHash = new string('m', 64), FileSize = 1 };
            photo.Locations.Add(new PhotoLocation { LibraryRoot = r, RelPath = "gone.png", Status = "missing" });
            ctx.Photos.Add(photo); await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        Assert.Empty((await Svc(ctx2).SearchAsync([], [], null, 200)).Items);
    }
}
