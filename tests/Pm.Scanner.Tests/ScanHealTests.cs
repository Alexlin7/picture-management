// tests/Pm.Scanner.Tests/ScanHealTests.cs
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Microsoft.Data.Sqlite;
using Xunit;

public class ScanHealTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-heal-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-healroot-{Guid.NewGuid():N}");
    private readonly string _thumbDir;
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public ScanHealTests()
    {
        Directory.CreateDirectory(_root);
        _thumbDir = Path.Combine(_root, "_thumbs");
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    private LibraryScanner MakeScanner(PmDbContext ctx)
    {
        var thumbs = new ThumbnailService(new ThumbnailOptions { Dir = _thumbDir, MaxEdge = 64 });
        return new LibraryScanner(ctx, new Sha256FileHasher(), new ExifImageMetadataReader(), thumbs,
            new ImageReprocessor(new ExifImageMetadataReader(), thumbs));
    }

    [Fact]
    public async Task Rescan_heals_width_null_photo()
    {
        // Arrange:寫真實 png,先建一筆「半殘」photo(width=null、無縮圖)+ present location,mtime 對齊檔案。
        var rel = "a.png";
        var file = Path.Combine(_root, rel);
        using (var img = new Image<Rgba32>(4, 2)) img.SaveAsPng(file);
        var hash = await new Sha256FileHasher().HashFileAsync(file);
        var mtime = (DateTimeOffset)new FileInfo(file).LastWriteTimeUtc;
        long rootId, photoId;
        await using (var ctx = NewContext())
        {
            var root = new LibraryRoot { Name = "t", AbsPath = _root };
            ctx.LibraryRoots.Add(root);
            await ctx.SaveChangesAsync();
            rootId = root.Id;
            var photo = new Photo { FileHash = hash, FileSize = new FileInfo(file).Length };  // Width=null
            ctx.Photos.Add(photo);
            await ctx.SaveChangesAsync();
            photoId = photo.Id;
            ctx.PhotoLocations.Add(new PhotoLocation
            {
                PhotoId = photoId, LibraryRootId = rootId, RelPath = rel,
                Status = "present", Mtime = mtime,
                FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        // Act:重掃(快路徑會判定 size+mtime 沒變)。
        ScanResult result;
        await using (var ctx = NewContext())
            result = await MakeScanner(ctx).ScanRootAsync(rootId, enqueueTagging: true);

        // Assert:被痊癒。
        Assert.Equal(1, result.Healed);
        await using var verify = NewContext();
        var healed = await verify.Photos.FindAsync(photoId);
        Assert.Equal(4, healed!.Width);
        Assert.Equal("image/png", healed.Mime);
        Assert.True(File.Exists(Path.Combine(_thumbDir, hash[..2], hash[2..4], hash + ".webp")));
        Assert.True(await verify.TaggingJobs.AnyAsync(j => j.PhotoId == photoId));
    }
}
