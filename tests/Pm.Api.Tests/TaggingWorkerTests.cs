using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pm.Api;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Ml;
using Xunit;

namespace Pm.Api.Tests;

public class TaggingWorkerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-worker-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-workerroot-{Guid.NewGuid():N}");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public TaggingWorkerTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "a.png"), "x");   // 路徑存在即可(fake 不真讀)
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

    private sealed class FakeTagger : IWd14Tagger
    {
        public Task<IReadOnlyList<(string Name, string Kind, float Conf)>> TagAsync(string p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(string, string, float)>>(new[]
            {
                ("1girl", "general", 0.95f),
                ("hakurei_reimu", "character", 0.91f),
            });
    }

    private sealed class ThrowingTagger : IWd14Tagger
    {
        public Task<IReadOnlyList<(string Name, string Kind, float Conf)>> TagAsync(string p, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    private async Task<long> SeedPendingJob()
    {
        await using var ctx = NewContext();
        var root = new LibraryRoot { Name = "t", AbsPath = _root };
        var photo = new Photo { FileHash = new string('a', 64), FileSize = 1 };
        photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = "a.png", Status = "present" });
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
        ctx.TaggingJobs.Add(new TaggingJob { PhotoId = photo.Id });
        await ctx.SaveChangesAsync();
        return photo.Id;
    }

    private TaggingWorker Worker(IWd14Tagger tagger) =>
        new(new DummyScopeFactory(), tagger, NullLogger<TaggingWorker>.Instance);

    // ProcessNextAsync 直接收 db,不用 scope factory;給個不會被呼叫的 dummy。
    private sealed class DummyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException();
    }

    [Fact]
    public async Task Processes_pending_job_writes_tags_and_marks_done()
    {
        var photoId = await SeedPendingJob();

        await using var ctx = NewContext();
        var ok = await Worker(new FakeTagger()).ProcessNextAsync(ctx, default);
        Assert.True(ok);

        await using var verify = NewContext();
        Assert.Equal("done", (await verify.TaggingJobs.SingleAsync()).State);
        Assert.Equal(2, await verify.PhotoTags.CountAsync(pt => pt.PhotoId == photoId && pt.Source == "wd14"));
        var charTag = await verify.Tags.SingleAsync(t => t.Name == "hakurei_reimu");
        Assert.Equal("character", charTag.Kind);
    }

    [Fact]
    public async Task Reuses_existing_tag_case_insensitively_no_duplicate()
    {
        var photoId = await SeedPendingJob();

        // 預先放一個「大小寫不同」的既有 tag(模擬手動建立的 1Girl)。
        long existingId;
        await using (var seed = NewContext())
        {
            var t = new Tag { Name = "1Girl", Kind = "manual" };
            seed.Tags.Add(t);
            await seed.SaveChangesAsync();
            existingId = t.Id;
        }

        await using var ctx = NewContext();
        await Worker(new FakeTagger()).ProcessNextAsync(ctx, default);   // fake 產 "1girl"

        await using var verify = NewContext();
        // 不分大小寫比對下,"1girl" 只能有一顆(不因大小寫不同就建第二顆)。
        Assert.Equal(1, await verify.Tags.CountAsync(t => t.Name.ToLower() == "1girl"));
        // wd14 的 photo_tag 應掛到「既有那顆」,而非新建的。
        Assert.True(await verify.PhotoTags.AnyAsync(
            pt => pt.PhotoId == photoId && pt.TagId == existingId && pt.Source == "wd14"));
    }

    [Fact]
    public async Task No_pending_returns_false()
    {
        await using var ctx = NewContext();
        Assert.False(await Worker(new FakeTagger()).ProcessNextAsync(ctx, default));
    }

    [Fact]
    public async Task Failure_marks_error_and_increments_attempts()
    {
        await SeedPendingJob();

        await using var ctx = NewContext();
        await Worker(new ThrowingTagger()).ProcessNextAsync(ctx, default);

        await using var verify = NewContext();
        var job = await verify.TaggingJobs.SingleAsync();
        Assert.Equal("error", job.State);
        Assert.Equal(1, job.Attempts);
    }
}
