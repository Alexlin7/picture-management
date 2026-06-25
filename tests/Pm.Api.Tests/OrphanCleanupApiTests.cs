using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Api.Tests;

public class OrphanCleanupApiTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pm-orphan-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public OrphanCleanupApiTests()
    {
        var db = _db;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // 生產等價:不開 FK(BuildSqliteConnectionString 也沒帶),確保 purge 端點層自保正確,
                    // 而非被 DB FK cascade 遮蔽。改此測試類別在 FK off 下跑是關鍵迴歸守衛。
                    ["ConnectionStrings:Pm"] = $"Data Source={db}"
                })));
    }

    // 寫一筆無 location 的孤兒 photo,回其 id。
    private async Task<long> SeedOrphanAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var p = new Photo { FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ImportedAt = DateTimeOffset.UtcNow };
        ctx.Photos.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    // 寫一筆有 present location 的正常 photo,回其 id。
    private async Task<long> SeedWithLocationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var root = new LibraryRoot { Name = "r", AbsPath = @"C:\x" };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        var p = new Photo { FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ImportedAt = DateTimeOffset.UtcNow };
        ctx.Photos.Add(p);
        await ctx.SaveChangesAsync();
        ctx.PhotoLocations.Add(new PhotoLocation { PhotoId = p.Id, LibraryRootId = root.Id, RelPath = "a.png", Status = "present", FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task Preview_lists_only_orphans()
    {
        var orphanId = await SeedOrphanAsync();
        await SeedWithLocationAsync();   // 不該出現
        var client = _factory.CreateClient();

        var res = await client.GetFromJsonAsync<OrphanPreview>("/api/maintenance/orphan-photos");

        Assert.NotNull(res);
        Assert.Equal(1, res!.Count);
        Assert.Equal(new[] { orphanId }, res.Ids);
    }

    private sealed record OrphanPreview(int Count, long[] Ids);

    [Fact]
    public async Task Purge_deletes_orphans_cascade_and_thumb_but_keeps_located()
    {
        // 孤兒帶 photo_tag + tagging_job + 縮圖檔
        // 註:孤兒恆零 location(定義即 !p.Locations.Any()),故 location cascade 在此不可達、不另驗;
        //     cascade 設定本身(PhotoLocation/PhotoTag/TaggingJob → Photo)在 PmDbContext 已正確配置。
        long orphanId; string orphanHash; string? thumbDir = null;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            var p = new Photo { FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ImportedAt = DateTimeOffset.UtcNow };
            ctx.Photos.Add(p);
            await ctx.SaveChangesAsync();
            orphanId = p.Id; orphanHash = p.FileHash;
            var tag = new Tag { Name = "x", Kind = "manual" };
            ctx.Tags.Add(tag);
            await ctx.SaveChangesAsync();
            ctx.PhotoTags.Add(new PhotoTag { PhotoId = p.Id, TagId = tag.Id, Source = "manual" });
            ctx.TaggingJobs.Add(new TaggingJob { PhotoId = p.Id, State = "pending", EnqueuedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await ctx.SaveChangesAsync();

            // 造一個縮圖檔
            var thumbs = scope.ServiceProvider.GetRequiredService<Pm.Scanner.IThumbnailService>();
            var tp = thumbs.PathFor(orphanHash);
            thumbDir = Path.GetDirectoryName(tp);
            Directory.CreateDirectory(thumbDir!);
            await File.WriteAllTextAsync(tp, "fake");
        }
        var locatedId = await SeedWithLocationAsync();   // 不該被刪
        var client = _factory.CreateClient();

        var resp = await client.DeleteAsync("/api/maintenance/orphan-photos");
        resp.EnsureSuccessStatusCode();
        var res = await resp.Content.ReadFromJsonAsync<OrphanPurge>();

        Assert.NotNull(res);
        Assert.Equal(1, res!.Purged);
        Assert.Equal(1, res.ThumbsDeleted);
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            Assert.False(ctx.Photos.Any(p => p.Id == orphanId));               // 孤兒消失
            Assert.True(ctx.Photos.Any(p => p.Id == locatedId));               // 正常的留著
            Assert.False(ctx.PhotoTags.Any(pt => pt.PhotoId == orphanId));     // cascade
            Assert.False(ctx.TaggingJobs.Any(j => j.PhotoId == orphanId));     // cascade
            var thumbs = scope.ServiceProvider.GetRequiredService<Pm.Scanner.IThumbnailService>();
            Assert.False(File.Exists(thumbs.PathFor(orphanHash)));             // 縮圖刪除
        }

        // test hygiene:清掉造在 system temp 的縮圖目錄(僅存在時刪)
        if (thumbDir is not null && Directory.Exists(thumbDir)) Directory.Delete(thumbDir, recursive: true);
    }

    [Fact]
    public async Task Purge_with_no_orphans_is_idempotent()
    {
        await SeedWithLocationAsync();
        var client = _factory.CreateClient();

        var resp = await client.DeleteAsync("/api/maintenance/orphan-photos");
        resp.EnsureSuccessStatusCode();
        var res = await resp.Content.ReadFromJsonAsync<OrphanPurge>();

        Assert.NotNull(res);
        Assert.Equal(0, res!.Purged);
        Assert.Equal(0, res.ThumbsDeleted);
    }

    private sealed record OrphanPurge(int Purged, int ThumbsDeleted);

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
