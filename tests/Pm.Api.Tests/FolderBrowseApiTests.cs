using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Api.Tests;

public class FolderBrowseApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-browseapi-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public FolderBrowseApiTests()
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

    private record RootDto(long Id, string Name, int PhotoCount);
    private record NodeDto(string Name, string RelPath, int PhotoCount, List<NodeDto>? Children);

    private async Task<long> Seed()
    {
        _ = _factory.CreateClient();   // 觸發 Migrate
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var root = new LibraryRoot { Name = "圖庫", AbsPath = @"D:\圖庫" };
        db.LibraryRoots.Add(root); await db.SaveChangesAsync();

        void Add(string hash, string rel)
        {
            var p = new Photo { FileHash = hash.PadRight(64, '0'), FileSize = 1 };
            p.Locations.Add(new PhotoLocation { LibraryRootId = root.Id, RelPath = rel, Status = "present" });
            db.Photos.Add(p);
        }
        Add("a", "Pixiv/2024/a.png");
        Add("b", "Pixiv/2024/b.png");
        await db.SaveChangesAsync();
        return root.Id;
    }

    [Fact]
    public async Task FolderRoots_returns_single_root_with_correct_photo_count()
    {
        _ = await Seed();
        var client = _factory.CreateClient();

        var roots = await client.GetFromJsonAsync<List<RootDto>>("/api/folder-roots");
        Assert.Single(roots!);
        Assert.Equal(2, roots![0].PhotoCount);
    }

    [Fact]
    public async Task FolderTree_returns_expected_shape()
    {
        var rootId = await Seed();
        var client = _factory.CreateClient();

        var tree = await client.GetFromJsonAsync<NodeDto>($"/api/roots/{rootId}/folder-tree");
        Assert.Equal("圖庫", tree!.Name);
        Assert.Equal(2, tree.PhotoCount);
        var pixiv = tree.Children!.Single(c => c.Name == "Pixiv");
        Assert.Equal("Pixiv/2024", pixiv.Children!.Single().RelPath);
    }

    [Fact]
    public async Task FolderTree_returns_404_for_unknown_root()
    {
        _ = await Seed();
        var client = _factory.CreateClient();

        var missing = await client.GetAsync("/api/roots/99999/folder-tree");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private record FolderTagDto(string Name, string Kind, int Count);

    [Fact]
    public async Task Folder_tags_and_scoped_search_endpoints_work()
    {
        var rootId = await Seed();   // 兩張圖都在 Pixiv/2024,無 tag
        var client = _factory.CreateClient();

        // 夾內查詢:Pixiv/2024 含 2 張
        var page = await (await client.PostAsJsonAsync("/api/search",
            new { rootId, pathPrefix = "Pixiv/2024" }))
            .Content.ReadFromJsonAsync<Page>();
        Assert.Equal(2, page!.Items.Count);

        // 夾外前綴:Twitter 無圖
        var empty = await (await client.PostAsJsonAsync("/api/search/count",
            new { rootId, pathPrefix = "Twitter" }))
            .Content.ReadAsStringAsync();
        Assert.Contains("\"total\":0", empty);

        // folder-tags 端點可呼叫(seed 無 tag → 空陣列)
        var tags = await client.GetFromJsonAsync<List<FolderTagDto>>(
            $"/api/browse/folder-tags?rootId={rootId}&path=Pixiv/2024");
        Assert.NotNull(tags);
        Assert.Empty(tags!);
    }

    private record Page(List<PageItem> Items, long? NextCursor);
    private record PageItem(long Id, string FileHash);
}
