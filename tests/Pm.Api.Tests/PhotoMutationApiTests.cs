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

public class PhotoMutationApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-pmutapi-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public PhotoMutationApiTests()
    {
        var db = _dbPath;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={db};Foreign Keys=True"
                })));
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private record ArchiveResult(int Archived);
    private record TagView(long Id, string Name, string Kind, string Source, float? Confidence);

    private long SeedPhoto(int locations = 2)
    {
        _ = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var root = new LibraryRoot { Name = "r", AbsPath = "/tmp/r-" + Guid.NewGuid() };
        db.LibraryRoots.Add(root);
        db.SaveChanges();
        var photo = new Photo { FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N") };
        db.Photos.Add(photo);
        db.SaveChanges();
        for (int i = 0; i < locations; i++)
            db.PhotoLocations.Add(new PhotoLocation { PhotoId = photo.Id, LibraryRootId = root.Id, RelPath = $"f{i}.png", Status = "present" });
        // 掛一個 wd14 tag,驗證軟刪保留 tags
        var tag = new Tag { Name = "keepme", Kind = "general" };
        db.Tags.Add(tag);
        db.SaveChanges();
        db.PhotoTags.Add(new PhotoTag { PhotoId = photo.Id, TagId = tag.Id, Source = "wd14", Confidence = 0.7f });
        db.SaveChanges();
        return photo.Id;
    }

    [Fact]
    public async Task Archive_soft_deletes_locations_keeps_photo_and_tags()
    {
        var id = SeedPhoto(locations: 2);
        var client = _factory.CreateClient();

        var resp = await client.PostAsync($"/api/photos/{id}/archive", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ArchiveResult>();
        Assert.Equal(2, body!.Archived);

        // photo + tags 仍在,locations 全 archived
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        Assert.True(await db.Photos.AnyAsync(p => p.Id == id));
        Assert.True(await db.PhotoTags.AnyAsync(pt => pt.PhotoId == id));
        Assert.True(await db.PhotoLocations.Where(l => l.PhotoId == id).AllAsync(l => l.Status == "archived"));
    }

    [Fact]
    public async Task Archive_unknown_photo_returns_404()
    {
        _ = _factory.CreateClient();
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/photos/99999/archive", null)).StatusCode);
    }

    [Fact]
    public async Task Purge_hard_deletes_photo_cascade_then_404()
    {
        var id = SeedPhoto();
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/photos/{id}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            Assert.False(await db.Photos.AnyAsync(p => p.Id == id));
            Assert.False(await db.PhotoLocations.AnyAsync(l => l.PhotoId == id));
            Assert.False(await db.PhotoTags.AnyAsync(pt => pt.PhotoId == id));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/photos/{id}")).StatusCode);
    }

    [Fact]
    public async Task Add_manual_tag_creates_then_idempotent()
    {
        var id = SeedPhoto();
        var client = _factory.CreateClient();

        var r1 = await client.PostAsJsonAsync($"/api/photos/{id}/tags", new { name = "favourite" });
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var v1 = await r1.Content.ReadFromJsonAsync<TagView>();
        Assert.Equal("favourite", v1!.Name);
        Assert.Equal("manual", v1.Kind);
        Assert.Equal("manual", v1.Source);
        Assert.Null(v1.Confidence);

        // 重複呼叫 → idempotent,回相同 tag,且 photo_tag 只有一筆
        var r2 = await client.PostAsJsonAsync($"/api/photos/{id}/tags", new { name = "favourite" });
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var v2 = await r2.Content.ReadFromJsonAsync<TagView>();
        Assert.Equal(v1.Id, v2!.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        Assert.Equal(1, await db.PhotoTags.CountAsync(pt => pt.PhotoId == id && pt.TagId == v1.Id));
    }

    [Fact]
    public async Task Add_manual_tag_unknown_photo_returns_404()
    {
        _ = _factory.CreateClient();
        var client = _factory.CreateClient();
        var r = await client.PostAsJsonAsync("/api/photos/99999/tags", new { name = "x" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Remove_manual_tag_then_404()
    {
        var id = SeedPhoto();
        var client = _factory.CreateClient();
        var v = await (await client.PostAsJsonAsync($"/api/photos/{id}/tags", new { name = "temp" }))
            .Content.ReadFromJsonAsync<TagView>();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/photos/{id}/tags/{v!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/photos/{id}/tags/{v.Id}")).StatusCode);
    }
}
