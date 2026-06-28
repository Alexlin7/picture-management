using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Pm.Data;
using Pm.Data.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

// /api/photos/{id}/file —— lightbox 原圖串流端點:唯讀串流原檔、可下載。
public class PhotoFileApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-fileapi-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-fileroot-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public PhotoFileApiTests()
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

    private async Task<long> SeedPhotoAsync(string rel, string status = "present", bool writeFile = true)
    {
        if (writeFile)
        {
            var file = Path.Combine(_root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var img = new Image<Rgba32>(8, 5);
            img.SaveAsPng(file);
        }
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var root = new LibraryRoot { Name = "t", AbsPath = _root };
        db.LibraryRoots.Add(root);
        await db.SaveChangesAsync();
        var photo = new Photo { FileHash = "ff" + new string('0', 62), Mime = "image/png", Width = 8, Height = 5 };
        db.Photos.Add(photo);
        await db.SaveChangesAsync();
        db.PhotoLocations.Add(new PhotoLocation
        {
            PhotoId = photo.Id, LibraryRootId = root.Id, RelPath = rel,
            Status = status, FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return photo.Id;
    }

    [Fact]
    public async Task File_returns_original_bytes_with_stored_mime()
    {
        var id = await SeedPhotoAsync("a.png");
        var client = _factory.CreateClient();

        var resp = await client.GetAsync($"/api/photos/{id}/file");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("image/png", resp.Content.Headers.ContentType?.MediaType);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
        // PNG magic number,確認真的串到原檔內容
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
    }

    [Fact]
    public async Task File_with_download_flag_sets_attachment_disposition()
    {
        var id = await SeedPhotoAsync("pic name.png");
        var client = _factory.CreateClient();

        var resp = await client.GetAsync($"/api/photos/{id}/file?download=true");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("attachment", resp.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("pic name.png", resp.Content.Headers.ContentDisposition?.FileNameStar
            ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact]
    public async Task File_missing_photo_returns_404()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/photos/99999/file");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task File_missing_on_disk_returns_404()
    {
        var id = await SeedPhotoAsync("gone.png", writeFile: false);
        var client = _factory.CreateClient();
        var resp = await client.GetAsync($"/api/photos/{id}/file");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
