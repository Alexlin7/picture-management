using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Api.Tests;

public class TaggingRequeueApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-requeue-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public TaggingRequeueApiTests()
    {
        var db = _dbPath;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={db};Foreign Keys=True",
                    ["Inference:Wd14:Enabled"] = "false"
                })));
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private record RequeueResult(int Matched, int ClearedTags, int JobsCreated, int JobsUpdated);

    private (long RootId, long PhotoId) SeedPhoto(string status = "present")
    {
        _ = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();

        var root = new LibraryRoot { Name = "r", AbsPath = "/tmp/r-" + Guid.NewGuid() };
        db.LibraryRoots.Add(root);
        db.SaveChanges();

        var photo = new Photo { FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), FileSize = 1 };
        db.Photos.Add(photo);
        db.SaveChanges();

        db.PhotoLocations.Add(new PhotoLocation
        {
            PhotoId = photo.Id,
            LibraryRootId = root.Id,
            RelPath = $"{Guid.NewGuid():N}.png",
            Status = status
        });
        db.SaveChanges();
        return (root.Id, photo.Id);
    }

    private long SeedTag(long photoId, string name, string source)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var tag = new Tag { Name = name, Kind = "general" };
        db.Tags.Add(tag);
        db.SaveChanges();
        db.PhotoTags.Add(new PhotoTag { PhotoId = photoId, TagId = tag.Id, Source = source, Confidence = source == "wd14" ? 0.7f : null });
        db.SaveChanges();
        return tag.Id;
    }

    [Fact]
    public async Task Retry_upserts_jobs_and_resets_existing_states()
    {
        var (_, done) = SeedPhoto();
        var (_, error) = SeedPhoto();
        var (_, running) = SeedPhoto();
        var (_, noJob) = SeedPhoto();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            db.TaggingJobs.AddRange(
                new TaggingJob { PhotoId = done, State = "done", Attempts = 4 },
                new TaggingJob { PhotoId = error, State = "error", Attempts = 2 },
                new TaggingJob { PhotoId = running, State = "running", Attempts = 1 });
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tag/requeue", new
        {
            mode = "retry",
            scope = new { photoIds = new[] { done, error, running, noJob } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RequeueResult>();
        Assert.Equal(4, result!.Matched);
        Assert.Equal(1, result.JobsCreated);
        Assert.Equal(3, result.JobsUpdated);

        using var verifyScope = _factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<PmDbContext>();
        var jobs = await verify.TaggingJobs.ToListAsync();
        Assert.Equal(4, jobs.Count);
        Assert.All(jobs, j =>
        {
            Assert.Equal("pending", j.State);
            Assert.Equal(0, j.Attempts);
        });
    }

    [Fact]
    public async Task Refresh_clears_only_target_wd14_tags_and_preserves_manual_or_path_tags()
    {
        var (_, target) = SeedPhoto();
        var (_, other) = SeedPhoto();
        var manualTag = SeedTag(target, "curated", "manual");
        var pathTag = SeedTag(target, "folder", "path");
        SeedTag(target, "old wd14", "wd14");
        SeedTag(other, "other wd14", "wd14");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tag/requeue", new
        {
            mode = "refresh",
            scope = new { photoIds = new[] { target } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RequeueResult>();
        Assert.Equal(1, result!.ClearedTags);
        Assert.Equal(1, result.JobsCreated);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        Assert.False(await db.PhotoTags.AnyAsync(pt => pt.PhotoId == target && pt.Source == "wd14"));
        Assert.True(await db.PhotoTags.AnyAsync(pt => pt.PhotoId == target && pt.TagId == manualTag && pt.Source == "manual"));
        Assert.True(await db.PhotoTags.AnyAsync(pt => pt.PhotoId == target && pt.TagId == pathTag && pt.Source == "path"));
        Assert.True(await db.PhotoTags.AnyAsync(pt => pt.PhotoId == other && pt.Source == "wd14"));
    }

    [Fact]
    public async Task Clear_removes_wd14_tags_without_creating_jobs()
    {
        var (_, photo) = SeedPhoto();
        SeedTag(photo, "old wd14", "wd14");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tag/requeue", new
        {
            mode = "clear",
            scope = new { photoIds = new[] { photo } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RequeueResult>();
        Assert.Equal(1, result!.ClearedTags);
        Assert.Equal(0, result.JobsCreated);
        Assert.Equal(0, result.JobsUpdated);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        Assert.False(await db.PhotoTags.AnyAsync(pt => pt.PhotoId == photo && pt.Source == "wd14"));
        Assert.False(await db.TaggingJobs.AnyAsync(j => j.PhotoId == photo));
    }

    [Fact]
    public async Task Root_scope_only_requeues_present_photos()
    {
        var (rootId, present) = SeedPhoto("present");
        var (_, missing) = SeedPhoto("missing");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            var missingLoc = await db.PhotoLocations.SingleAsync(l => l.PhotoId == missing);
            missingLoc.LibraryRootId = rootId;
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tag/requeue", new
        {
            mode = "retry",
            scope = new { root = rootId }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RequeueResult>();
        Assert.Equal(1, result!.Matched);

        using var verifyScope = _factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<PmDbContext>();
        Assert.True(await verify.TaggingJobs.AnyAsync(j => j.PhotoId == present));
        Assert.False(await verify.TaggingJobs.AnyAsync(j => j.PhotoId == missing));
    }

    [Fact]
    public async Task Clear_error_scope_returns_bad_request()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tag/requeue", new
        {
            mode = "clear",
            scope = new { error = true }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Multiple_scopes_return_bad_request()
    {
        var (_, photo) = SeedPhoto();
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tag/requeue", new
        {
            mode = "retry",
            scope = new { photoIds = new[] { photo }, all = true }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Retag_unknown_photo_returns_not_found()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/photos/999999/retag", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
