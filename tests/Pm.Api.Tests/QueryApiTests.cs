using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Api.Tests;

public class QueryApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-qapi-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-qroot-{Guid.NewGuid():N}");
    private readonly string _thumbs = Path.Combine(Path.GetTempPath(), $"pm-qthumbs-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public QueryApiTests()
    {
        Directory.CreateDirectory(_root);
        var db = _dbPath; var th = _thumbs;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={db};Foreign Keys=True",
                    ["Thumbnails:Dir"] = th
                })));
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        foreach (var d in new[] { _root, _thumbs }) if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    private record RootCreated(long Id);
    private record Item(long Id, string FileHash);
    private record Page(List<Item> Items, long? NextCursor);

    [Fact]
    public async Task Scan_then_search_thumb_and_detail()
    {
        using (var img = new Image<Rgba32>(40, 30)) await img.SaveAsPngAsync(Path.Combine(_root, "a.png"));

        var client = _factory.CreateClient();
        var root = await (await client.PostAsJsonAsync("/api/roots", new { name = "t", absPath = _root }))
            .Content.ReadFromJsonAsync<RootCreated>();
        await client.PostAsync($"/api/roots/{root!.Id}/scan", null);

        // 無條件瀏覽應回 1 張
        var page = await (await client.PostAsJsonAsync("/api/search", new { }))
            .Content.ReadFromJsonAsync<Page>();
        Assert.Single(page!.Items);
        var id = page.Items[0].Id;

        // 縮圖串流
        var thumb = await client.GetAsync($"/api/photos/{id}/thumb");
        Assert.Equal(HttpStatusCode.OK, thumb.StatusCode);
        Assert.Equal("image/webp", thumb.Content.Headers.ContentType!.MediaType);

        // 明細
        var detail = await client.GetAsync($"/api/photos/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.Content.ReadAsStringAsync();
        Assert.Contains("\"width\":40", body);

        // 不存在 → 404
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/photos/99999")).StatusCode);
    }
}
