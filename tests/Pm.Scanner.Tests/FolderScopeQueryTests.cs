using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class FolderScopeQueryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-scope-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public FolderScopeQueryTests()
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

    private PhotoQueryService Query(PmDbContext ctx) => new(ctx, new TagClosureService(ctx));

    private async Task AddPhoto(PmDbContext ctx, LibraryRoot root, string hash, string relPath, params long[] tagIds)
    {
        var photo = new Photo { FileHash = hash.PadRight(64, '0'), FileSize = 1 };
        photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = relPath, Status = "present" });
        foreach (var tid in tagIds) photo.Tags.Add(new PhotoTag { TagId = tid, Source = "manual" });
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task PathPrefix_scopes_recursively_and_avoids_sibling_prefix_collision()
    {
        long rootId;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "r", AbsPath = @"D:\r" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); rootId = r.Id;

            await AddPhoto(ctx, r, "a", "Pixiv/2024/a.png");
            await AddPhoto(ctx, r, "b", "Pixiv/2024/sub/b.png");   // 遞迴應含
            await AddPhoto(ctx, r, "c", "Pixiv2/c.png");           // 不可被 "Pixiv" 前綴誤中
            await AddPhoto(ctx, r, "d", "Twitter/d.png");
        }

        await using var ctx2 = NewContext();
        var svc = Query(ctx2);

        Assert.Equal(2, await svc.CountAsync([], [], rootId, "Pixiv"));        // a,b(遞迴);非 c
        Assert.Equal(2, await svc.CountAsync([], [], rootId, "Pixiv/2024"));   // a,b
        Assert.Equal(4, await svc.CountAsync([], [], rootId, ""));             // 整 root
        Assert.Equal(4, await svc.CountAsync([], [], rootId, null));           // null = 整 root
    }

    [Fact]
    public async Task PathPrefix_combines_with_tag_as_and()
    {
        long rootId, smile;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "r", AbsPath = @"D:\r" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); rootId = r.Id;
            var t = new Tag { Name = "smile", Kind = "general" };
            ctx.Tags.Add(t); await ctx.SaveChangesAsync(); smile = t.Id;

            await AddPhoto(ctx, r, "a", "Pixiv/2024/a.png", smile);   // 夾內 + 有 tag
            await AddPhoto(ctx, r, "b", "Pixiv/2024/b.png");          // 夾內 + 無 tag
            await AddPhoto(ctx, r, "c", "Twitter/c.png", smile);      // 有 tag + 夾外
        }

        await using var ctx2 = NewContext();
        var svc = Query(ctx2);

        Assert.Equal(1, await svc.CountAsync(["smile"], [], rootId, "Pixiv/2024"));  // 只剩 a
        var page = await svc.SearchAsync(["smile"], [], null, 200, rootId, "Pixiv/2024");
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task FolderTags_lists_only_tags_present_in_scope_with_counts()
    {
        long rootId, smile, dress;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "r", AbsPath = @"D:\r" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); rootId = r.Id;
            var s = new Tag { Name = "smile", Kind = "general" };
            var d = new Tag { Name = "dress", Kind = "general" };
            ctx.Tags.AddRange(s, d); await ctx.SaveChangesAsync(); smile = s.Id; dress = d.Id;

            await AddPhoto(ctx, r, "a", "Pixiv/2024/a.png", smile, dress);
            await AddPhoto(ctx, r, "b", "Pixiv/2024/b.png", smile);
            await AddPhoto(ctx, r, "c", "Twitter/c.png", dress);   // 夾外的 dress 不該灌進 Pixiv/2024 計數
        }

        await using var ctx2 = NewContext();
        var tags = await new FolderTreeService(ctx2).FolderTagsAsync(rootId, "Pixiv/2024");

        Assert.Equal(2, tags.Count);
        Assert.Equal("smile", tags[0].Name);    // count desc:smile=2 在前
        Assert.Equal(2, tags[0].Count);
        Assert.Equal("general", tags[0].Kind);  // Kind 應正確傳遞
        Assert.Equal("dress", tags[1].Name);    // dress=1(只算夾內 a)
        Assert.Equal(1, tags[1].Count);
        Assert.Equal("general", tags[1].Kind);  // Kind 應正確傳遞
    }
}
