using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class FolderTreeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-folder-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public FolderTreeTests()
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

    // 建一張在指定 root、指定相對路徑、present 的照片
    private async Task AddPhoto(PmDbContext ctx, LibraryRoot root, string hash, string relPath, string status = "present")
    {
        var photo = new Photo { FileHash = hash.PadRight(64, '0'), FileSize = 1 };
        photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = relPath, Status = status });
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task BuildTree_nests_folders_and_counts_recursively_distinct()
    {
        long rootId;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "圖庫", AbsPath = @"D:\圖庫" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); rootId = r.Id;

            await AddPhoto(ctx, r, "a", "Pixiv/2023/a.png");
            await AddPhoto(ctx, r, "b", "Pixiv/2024/b.png");
            await AddPhoto(ctx, r, "c", "Pixiv/2024/c.png");
            await AddPhoto(ctx, r, "d", "top.png");              // 直接放 root 底下
            await AddPhoto(ctx, r, "e", "Pixiv/2024/gone.png", status: "archived"); // 不算
        }

        await using var ctx2 = NewContext();
        var tree = await new FolderTreeService(ctx2).BuildTreeAsync(rootId);

        Assert.NotNull(tree);
        Assert.Equal("圖庫", tree!.Name);
        Assert.Equal("", tree.RelPath);
        Assert.Equal(4, tree.PhotoCount);                        // a,b,c,d(archived e 不算)

        var pixiv = tree.Children!.Single(c => c.Name == "Pixiv");
        Assert.Equal("Pixiv", pixiv.RelPath);
        Assert.Equal(3, pixiv.PhotoCount);                       // a,b,c

        var y2024 = pixiv.Children!.Single(c => c.Name == "2024");
        Assert.Equal("Pixiv/2024", y2024.RelPath);
        Assert.Equal(2, y2024.PhotoCount);                       // b,c(archived 不算)
        Assert.Null(y2024.Children);                             // 葉:無子資料夾
    }

    [Fact]
    public async Task BuildTree_same_named_subfolder_under_different_parents_stays_separate()
    {
        long rootId;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "r", AbsPath = @"D:\r" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); rootId = r.Id;

            await AddPhoto(ctx, r, "a", "Pixiv/蔚藍檔案/a.png");
            await AddPhoto(ctx, r, "b", "Twitter/蔚藍檔案/b.png");
        }

        await using var ctx2 = NewContext();
        var tree = await new FolderTreeService(ctx2).BuildTreeAsync(rootId);

        var pixivBa = tree!.Children!.Single(c => c.Name == "Pixiv").Children!.Single(c => c.Name == "蔚藍檔案");
        var twitterBa = tree.Children!.Single(c => c.Name == "Twitter").Children!.Single(c => c.Name == "蔚藍檔案");
        Assert.Equal("Pixiv/蔚藍檔案", pixivBa.RelPath);
        Assert.Equal("Twitter/蔚藍檔案", twitterBa.RelPath);     // 各自獨立節點,未合併
        Assert.Equal(1, pixivBa.PhotoCount);
        Assert.Equal(1, twitterBa.PhotoCount);
    }

    [Fact]
    public async Task BuildTree_returns_null_for_unknown_root()
    {
        await using var ctx = NewContext();
        Assert.Null(await new FolderTreeService(ctx).BuildTreeAsync(99999));
    }

    [Fact]
    public async Task BuildRoots_lists_each_root_with_distinct_present_count()
    {
        await using (var ctx = NewContext())
        {
            var r1 = new LibraryRoot { Name = "A", AbsPath = @"D:\a" };
            var r2 = new LibraryRoot { Name = "B", AbsPath = @"D:\b" };
            ctx.LibraryRoots.AddRange(r1, r2); await ctx.SaveChangesAsync();
            await AddPhoto(ctx, r1, "a", "x/a.png");
            await AddPhoto(ctx, r1, "b", "x/b.png");
            await AddPhoto(ctx, r2, "c", "y/c.png");
        }

        await using var ctx2 = NewContext();
        var roots = await new FolderTreeService(ctx2).BuildRootsAsync();

        Assert.Equal(2, roots.Count(r => r.PhotoCount > 0));
        Assert.Equal(2, roots.Single(r => r.Name == "A").PhotoCount);
        Assert.Equal(1, roots.Single(r => r.Name == "B").PhotoCount);
    }
}
