using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Scanner.Tests;

public class EnrichTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-enrich-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-enrichroot-{Guid.NewGuid():N}");
    private readonly string _thumbs = Path.Combine(Path.GetTempPath(), $"pm-enrichthumbs-{Guid.NewGuid():N}");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public EnrichTests()
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

    private async Task<long> SeedRoot()
    {
        await using var ctx = NewContext();
        var root = new LibraryRoot { Name = "t", AbsPath = _root };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        return root.Id;
    }

    [Fact]
    public async Task New_image_gets_dimensions_thumbnail_and_job()
    {
        using (var img = new Image<Rgba32>(640, 480))
            await img.SaveAsPngAsync(Path.Combine(_root, "pic.png"));
        var rootId = await SeedRoot();

        ScanResult result;
        await using (var ctx = NewContext())
            result = await Scanner(ctx).ScanRootAsync(rootId);

        Assert.Equal(1, result.NewPhotos);
        Assert.Equal(1, result.ThumbsGenerated);
        Assert.Equal(1, result.JobsQueued);

        await using var verify = NewContext();
        var photo = await verify.Photos.SingleAsync();
        Assert.Equal(640, photo.Width);
        Assert.Equal(480, photo.Height);
        Assert.Equal("image/png", photo.Mime);
        Assert.Equal(1, await verify.TaggingJobs.CountAsync(j => j.PhotoId == photo.Id));

        var thumbPath = new ThumbnailService(new ThumbnailOptions { Dir = _thumbs }).PathFor(photo.FileHash);
        Assert.True(File.Exists(thumbPath));
    }

    [Fact]
    public async Task Rescan_unchanged_decodable_image_backfills_missing_thumbnail()
    {
        using (var img = new Image<Rgba32>(320, 240))
            await img.SaveAsPngAsync(Path.Combine(_root, "pic.png"));
        var rootId = await SeedRoot();

        await using (var ctx = NewContext())
            await Scanner(ctx).ScanRootAsync(rootId);

        string thumbPath;
        await using (var verify = NewContext())
        {
            var hash = await verify.Photos.Select(p => p.FileHash).SingleAsync();
            thumbPath = new ThumbnailService(new ThumbnailOptions { Dir = _thumbs }).PathFor(hash);
        }

        File.Delete(thumbPath);
        Assert.False(File.Exists(thumbPath));

        ScanResult result;
        await using (var ctx = NewContext())
            result = await Scanner(ctx).ScanRootAsync(rootId, enqueueTagging: false);

        Assert.Equal(1, result.SkippedUnchanged);
        Assert.Equal(1, result.ThumbsGenerated);
        Assert.True(File.Exists(thumbPath));
    }

    [Fact]
    public async Task Bad_image_keeps_identity_but_no_thumb_or_job()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "broken.png"), "garbage");
        var rootId = await SeedRoot();

        ScanResult result;
        await using (var ctx = NewContext())
            result = await Scanner(ctx).ScanRootAsync(rootId);

        Assert.Equal(1, result.NewPhotos);        // 身分仍建立(有 bytes+hash)
        Assert.Equal(0, result.ThumbsGenerated);
        Assert.Equal(0, result.JobsQueued);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Photos.CountAsync());
        Assert.Null((await verify.Photos.SingleAsync()).Width);
        Assert.Equal(0, await verify.TaggingJobs.CountAsync());
    }
}
