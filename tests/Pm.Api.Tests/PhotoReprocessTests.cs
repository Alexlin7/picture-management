using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Pm.Data;
using Pm.Data.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class PhotoReprocessTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-repapi-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-reproot-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public PhotoReprocessTests()
    {
        Directory.CreateDirectory(_root);
        var dbPath = _dbPath;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Pm"] = $"Data Source={dbPath};Foreign Keys=True",
                ["Thumbnails:Dir"] = Path.Combine(_root, "_thumbs"),
            })));
    }
    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<long> SeedHalfDeadPhotoAsync(string rel)
    {
        var file = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using (var img = new Image<Rgba32>(6, 3)) img.SaveAsPng(file);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var root = new LibraryRoot { Name = "t", AbsPath = _root };
        db.LibraryRoots.Add(root);
        await db.SaveChangesAsync();
        var photo = new Photo { FileHash = "ee" + new string('0', 62) };  // Width=null
        db.Photos.Add(photo);
        await db.SaveChangesAsync();
        db.PhotoLocations.Add(new PhotoLocation
        {
            PhotoId = photo.Id, LibraryRootId = root.Id, RelPath = rel,
            Status = "present", FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return photo.Id;
    }

    [Fact]
    public async Task Reprocess_decodable_returns_decoded_and_fills_photo()
    {
        var id = await SeedHalfDeadPhotoAsync("a.png");
        var client = _factory.CreateClient();

        var resp = await client.PostAsync($"/api/photos/{id}/reprocess", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ReprocessBody>();
        Assert.True(body!.Decoded);
        Assert.True(body.ThumbGenerated);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var photo = await db.Photos.FindAsync(id);
        Assert.Equal(6, photo!.Width);
        Assert.Equal("image/png", photo.Mime);
    }

    [Fact]
    public async Task Reprocess_missing_photo_returns_404()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsync("/api/photos/99999/reprocess", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private sealed record ReprocessBody(bool Decoded, bool ThumbGenerated);
}
