using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class ScannerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-scan-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-root-{Guid.NewGuid():N}");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public ScannerTests()
    {
        Directory.CreateDirectory(_root);
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    private async Task<long> SeedRootAsync()
    {
        await using var ctx = NewContext();
        var root = new LibraryRoot { Name = "test", AbsPath = _root };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        return root.Id;
    }

    private void WriteImage(string relPath, string content)
    {
        var full = Path.Combine(_root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);   // 副檔名是 .png,內容當作 bytes 即可(本階段不解析圖)
    }

    [Fact]
    public async Task First_scan_indexes_and_dedups_by_hash()
    {
        WriteImage("a.png", "alpha");
        WriteImage("sub/b.png", "beta");
        WriteImage("sub/b_copy.png", "beta");   // 與 b.png 同內容 → 同 hash
        var rootId = await SeedRootAsync();

        await using var ctx = NewContext();
        var scanner = new LibraryScanner(ctx, new Sha256FileHasher());
        var result = await scanner.ScanRootAsync(rootId);

        Assert.Equal(3, result.FilesSeen);
        Assert.Equal(2, result.NewPhotos);       // alpha、beta 兩個身分
        Assert.Equal(3, result.NewLocations);    // 三個實體位置

        await using var verify = NewContext();
        Assert.Equal(2, await verify.Photos.CountAsync());
        Assert.Equal(3, await verify.PhotoLocations.CountAsync());

        // beta 的 photo 掛兩個位置
        var beta = await verify.Photos.Include("Locations")
            .SingleAsync(p => p.Locations.Count == 2);
        Assert.All(beta.Locations, l => Assert.Equal("present", l.Status));
    }

    [Fact]
    public async Task Missing_root_throws()
    {
        await using var ctx = NewContext();
        var scanner = new LibraryScanner(ctx, new Sha256FileHasher());
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanRootAsync(999));
    }
}
