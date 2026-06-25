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

    private long Seed()
    {
        _ = _factory.CreateClient();   // 觸發 Migrate
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var root = new LibraryRoot { Name = "圖庫", AbsPath = @"D:\圖庫" };
        db.LibraryRoots.Add(root); db.SaveChanges();

        void Add(string hash, string rel)
        {
            var p = new Photo { FileHash = hash.PadRight(64, '0'), FileSize = 1 };
            p.Locations.Add(new PhotoLocation { LibraryRootId = root.Id, RelPath = rel, Status = "present" });
            db.Photos.Add(p);
        }
        Add("a", "Pixiv/2024/a.png");
        Add("b", "Pixiv/2024/b.png");
        db.SaveChanges();
        return root.Id;
    }

    [Fact]
    public async Task Folder_roots_and_tree_endpoints_return_expected_shape()
    {
        var rootId = Seed();
        var client = _factory.CreateClient();

        var roots = await client.GetFromJsonAsync<List<RootDto>>("/api/folder-roots");
        Assert.Single(roots!);
        Assert.Equal(2, roots![0].PhotoCount);

        var tree = await client.GetFromJsonAsync<NodeDto>($"/api/roots/{rootId}/folder-tree");
        Assert.Equal("圖庫", tree!.Name);
        Assert.Equal(2, tree.PhotoCount);
        var pixiv = tree.Children!.Single(c => c.Name == "Pixiv");
        Assert.Equal("Pixiv/2024", pixiv.Children!.Single().RelPath);

        var missing = await client.GetAsync("/api/roots/99999/folder-tree");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
