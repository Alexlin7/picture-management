using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Scanner.Tests;

public class ReconcileTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-rec-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-recroot-{Guid.NewGuid():N}");
    private readonly string _thumbs = Path.Combine(Path.GetTempPath(), $"pm-recthumbs-{Guid.NewGuid():N}");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public ReconcileTests()
    {
        Directory.CreateDirectory(_root);
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        foreach (var d in new[] { _root, _thumbs }) if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    private LibraryScanner Scanner(PmDbContext ctx) =>
        new(ctx, new Sha256FileHasher(), new ExifImageMetadataReader(),
            new ThumbnailService(new ThumbnailOptions { Dir = _thumbs }));

    private async Task WriteImage(string rel, int seed)
    {
        var full = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var img = new Image<Rgba32>(16 + seed, 16);   // 不同尺寸 → 不同內容/hash
        await img.SaveAsPngAsync(full);
    }

    private async Task<long> SeedRoot()
    {
        await using var ctx = NewContext();
        var root = new LibraryRoot { Name = "t", AbsPath = _root };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        return root.Id;
    }

    [Fact]
    public async Task Deleted_file_marks_location_missing_but_keeps_photo()
    {
        await WriteImage("a.png", 1);
        await WriteImage("b.png", 2);
        var rootId = await SeedRoot();
        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);

        File.Delete(Path.Combine(_root, "a.png"));

        ScanResult result;
        await using (var ctx = NewContext()) result = await Scanner(ctx).ScanRootAsync(rootId);

        Assert.Equal(1, result.MarkedMissing);
        await using var verify = NewContext();
        Assert.Equal(2, await verify.Photos.CountAsync());   // photo 軟刪保留
        Assert.Equal(1, await verify.PhotoLocations.CountAsync(l => l.Status == "missing"));
        Assert.Equal(1, await verify.PhotoLocations.CountAsync(l => l.Status == "present"));
    }

    [Fact]
    public async Task Moved_file_keeps_one_present_location_identity_unchanged()
    {
        await WriteImage("old/a.png", 5);
        var rootId = await SeedRoot();
        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);

        File.Move(Path.Combine(_root, "old/a.png"), Path.Combine(_root, "new-a.png"));

        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Photos.CountAsync());   // 身分不變
        Assert.Equal(1, await verify.PhotoLocations.CountAsync(l => l.Status == "present"));
        Assert.Equal(1, await verify.PhotoLocations.CountAsync(l => l.Status == "missing"));
    }

    [Fact]
    public async Task Reappearing_file_is_auto_restored_to_present()
    {
        await WriteImage("a.png", 7);
        var rootId = await SeedRoot();
        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);

        var full = Path.Combine(_root, "a.png");
        var bytes = await File.ReadAllBytesAsync(full);
        File.Delete(full);
        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);   // → missing

        await File.WriteAllBytesAsync(full, bytes);   // 同內容回來
        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);   // → present

        await using var verify = NewContext();
        Assert.Equal(1, await verify.PhotoLocations.CountAsync(l => l.Status == "present"));
        Assert.Equal(0, await verify.PhotoLocations.CountAsync(l => l.Status == "missing"));
    }
}
