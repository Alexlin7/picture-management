using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Api.Tests;

public class PathTagApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-ptapi-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-ptroot-{Guid.NewGuid():N}");
    private readonly string _thumbs = Path.Combine(Path.GetTempPath(), $"pm-ptthumbs-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public PathTagApiTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "vspo"));
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

    [Fact]
    public async Task Pending_then_confirm_tags_photos()
    {
        using (var img = new Image<Rgba32>(20, 20))
            await img.SaveAsPngAsync(Path.Combine(_root, "vspo", "a.png"));

        var client = _factory.CreateClient();
        var root = await (await client.PostAsJsonAsync("/api/roots", new { name = "t", absPath = _root }))
            .Content.ReadFromJsonAsync<RootCreated>();
        await client.PostAsync($"/api/roots/{root!.Id}/scan", null);

        var pending = await client.GetStringAsync($"/api/roots/{root.Id}/pending-segments");
        Assert.Contains("vspo", pending);

        var confirm = await client.PostAsJsonAsync("/api/path-rules",
            new { rootId = root.Id, segment = "vspo", action = "map_to_tag", tagName = "vspo" });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        // 確認後 vspo 不再待確認
        var pending2 = await client.GetStringAsync($"/api/roots/{root.Id}/pending-segments");
        Assert.DoesNotContain("\"vspo\"", pending2);
    }
}
