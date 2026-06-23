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

    private CountingSaveContext NewCountingContext() =>
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
    public async Task First_scan_batches_new_files_and_dedups_within_batch()
    {
        WriteImage("a.png", "alpha");
        WriteImage("sub/b.png", "beta");
        WriteImage("sub/b_copy.png", "beta");
        var rootId = await SeedRootAsync();

        await using var ctx = NewCountingContext();
        var result = await new LibraryScanner(ctx, new Sha256FileHasher()).ScanRootAsync(rootId);

        Assert.Equal(3, result.FilesSeen);
        Assert.Equal(2, result.NewPhotos);
        Assert.Equal(3, result.NewLocations);
        Assert.Equal(2, ctx.SaveChangesCalls);

        await using var verify = NewContext();
        Assert.Equal(2, await verify.Photos.CountAsync());
        Assert.Equal(3, await verify.PhotoLocations.CountAsync());
    }

    [Fact]
    public async Task First_scan_releases_batched_import_entities_from_change_tracker()
    {
        for (var i = 0; i < 501; i++)
            WriteImage($"img-{i:000}.png", $"unique-{i}");
        var rootId = await SeedRootAsync();

        await using var ctx = NewContext();
        var result = await new LibraryScanner(ctx, new Sha256FileHasher()).ScanRootAsync(rootId);

        Assert.Equal(501, result.FilesSeen);
        Assert.Equal(501, result.NewPhotos);
        Assert.Equal(501, result.NewLocations);
        Assert.Empty(ctx.ChangeTracker.Entries<Photo>());
        Assert.Empty(ctx.ChangeTracker.Entries<PhotoLocation>());
        Assert.Empty(ctx.ChangeTracker.Entries<TaggingJob>());
    }

    [Fact]
    public async Task Reconcile_marks_missing_above_sqlite_variable_limit_without_crashing()
    {
        // SQLite 變數上限 32766。對帳不可把整包集合塞進單一 IN(`NOT IN (@p1...@pN)` 或 `Id IN (...)`)。
        // 直接 seed 超過上限的 present location,再掃「空 root」→ 全數該標 missing,且不可 'too many SQL variables'。
        const int n = 33_000;
        var rootId = await SeedRootAsync();
        await using (var seed = NewContext())
        {
            var photo = new Photo { FileHash = "seedhash", FileSize = 1 };
            seed.Photos.Add(photo);
            await seed.SaveChangesAsync();
            for (var i = 0; i < n; i++)
                seed.PhotoLocations.Add(new PhotoLocation
                {
                    PhotoId = photo.Id, LibraryRootId = rootId, RelPath = $"gone/f{i}.png",
                    Status = "present", FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
                });
            await seed.SaveChangesAsync();
        }

        // _root 目錄是空的(沒寫任何檔)→ 這輪看不到任何 location,n 個全部該轉 missing。
        await using var ctx = NewContext();
        var result = await new LibraryScanner(ctx, new Sha256FileHasher()).ScanRootAsync(rootId);

        Assert.Equal(0, result.FilesSeen);
        Assert.Equal(n, result.MarkedMissing);

        await using var verify = NewContext();
        Assert.Equal(n, await verify.PhotoLocations.CountAsync(l => l.Status == "missing"));
    }

    [Fact]
    public async Task Batched_scan_keeps_per_file_metadata_errors_isolated()
    {
        WriteImage("bad.png", "bad");
        WriteImage("ok.png", "ok");
        var rootId = await SeedRootAsync();

        await using var ctx = NewContext();
        var scanner = new LibraryScanner(
            ctx,
            new Sha256FileHasher(),
            new ThrowingMetadataReader("bad.png"),
            new NoopThumbnailService());
        var result = await scanner.ScanRootAsync(rootId);

        Assert.Equal(2, result.FilesSeen);
        Assert.Equal(1, result.Errors);
        Assert.Equal(1, result.NewPhotos);
        Assert.Equal(1, result.NewLocations);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Photos.CountAsync());
        Assert.Equal(1, await verify.PhotoLocations.CountAsync());
        Assert.Equal("ok.png", await verify.PhotoLocations.Select(l => l.RelPath).SingleAsync());
    }

    [Fact]
    public async Task Missing_root_throws()
    {
        await using var ctx = NewContext();
        var scanner = new LibraryScanner(ctx, new Sha256FileHasher());
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanRootAsync(999));
    }

    [Fact]
    public async Task Rescan_unchanged_uses_fast_path()
    {
        WriteImage("a.png", "alpha");
        var rootId = await SeedRootAsync();

        await using (var ctx = NewContext())
            await new LibraryScanner(ctx, new Sha256FileHasher()).ScanRootAsync(rootId);

        // 第二輪:檔案沒動 → 應走快路徑、不新增身分/位置
        await using var ctx2 = NewContext();
        var counting = new CountingHasher(new Sha256FileHasher());
        var result = await new LibraryScanner(ctx2, counting).ScanRootAsync(rootId);

        Assert.Equal(1, result.FilesSeen);
        Assert.Equal(0, result.NewPhotos);
        Assert.Equal(0, result.NewLocations);
        Assert.Equal(1, result.SkippedUnchanged);
        Assert.Equal(0, counting.Calls);          // 關鍵:沒重算 hash
    }

    [Fact]
    public async Task Rescan_unchanged_batches_fast_path_save()
    {
        WriteImage("a.png", "alpha");
        WriteImage("b.png", "beta");
        WriteImage("c.png", "gamma");
        var rootId = await SeedRootAsync();

        await using (var ctx = NewContext())
            await new LibraryScanner(ctx, new Sha256FileHasher()).ScanRootAsync(rootId);

        await using var ctx2 = NewCountingContext();
        var result = await new LibraryScanner(ctx2, new CountingHasher(new Sha256FileHasher())).ScanRootAsync(rootId);

        Assert.Equal(3, result.SkippedUnchanged);
        Assert.Equal(1, ctx2.SaveChangesCalls);
        Assert.Empty(ctx2.ChangeTracker.Entries<Photo>());
    }

    [Fact]
    public async Task Changed_content_rehashes_and_reassigns_identity()
    {
        WriteImage("a.png", "alpha");
        var rootId = await SeedRootAsync();
        await using (var ctx = NewContext())
            await new LibraryScanner(ctx, new Sha256FileHasher()).ScanRootAsync(rootId);

        // 改內容 + 推進 mtime
        var full = Path.Combine(_root, "a.png");
        await File.WriteAllTextAsync(full, "ALPHA-v2");
        File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddMinutes(5));

        await using var ctx2 = NewContext();
        var result = await new LibraryScanner(ctx2, new Sha256FileHasher()).ScanRootAsync(rootId);

        Assert.Equal(1, result.NewPhotos);        // 新內容 = 新身分
        Assert.Equal(0, result.NewLocations);     // 還是同一個位置,只是換了 photo
        Assert.Equal(0, result.SkippedUnchanged);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.PhotoLocations.CountAsync());   // 位置沒重複
        Assert.Equal(2, await verify.Photos.CountAsync());           // 舊+新兩個身分
        var loc = await verify.PhotoLocations.Include(l => l.Photo).SingleAsync();
        Assert.Equal("ALPHA-v2".Length, loc.Photo.FileSize);         // 指向新身分
    }

    [Fact]
    public async Task Rescan_is_idempotent()
    {
        WriteImage("a.png", "alpha");
        WriteImage("b.png", "beta");
        var rootId = await SeedRootAsync();

        for (int i = 0; i < 3; i++)
            await using (var ctx = NewContext())
                await new LibraryScanner(ctx, new Sha256FileHasher()).ScanRootAsync(rootId);

        await using var verify = NewContext();
        Assert.Equal(2, await verify.Photos.CountAsync());
        Assert.Equal(2, await verify.PhotoLocations.CountAsync());
    }

    private sealed class CountingSaveContext(DbContextOptions<PmDbContext> options) : PmDbContext(options)
    {
        public int SaveChangesCalls { get; private set; }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        {
            SaveChangesCalls++;
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        }
    }
}

file sealed class CountingHasher(IFileHasher inner) : IFileHasher
{
    public int Calls { get; private set; }
    public Task<string> HashFileAsync(string absPath, CancellationToken ct = default)
    {
        Calls++;
        return inner.HashFileAsync(absPath, ct);
    }
}

file sealed class ThrowingMetadataReader(string relPath) : IImageMetadataReader
{
    public ImageMeta Read(string absPath)
    {
        if (Path.GetFileName(absPath) == relPath) throw new IOException("metadata unavailable");
        return new ImageMeta(null, null, null, null, null, null, null, null);
    }
}

file sealed class NoopThumbnailService : IThumbnailService
{
    public string PathFor(string hash) => hash;
    public Task<string?> GenerateAsync(string absPath, string hash, CancellationToken ct = default) =>
        Task.FromResult<string?>(hash);
}
