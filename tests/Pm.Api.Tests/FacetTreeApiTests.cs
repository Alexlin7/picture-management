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

public class FacetTreeApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-facetapi-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public FacetTreeApiTests()
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

    private record Node(string Name, string Kind, int Count, bool Multi, List<Node>? Children);
    private record TreeDto(List<Node> Tree, List<Node> Rootless,
        List<List<System.Text.Json.JsonElement>> General, List<List<System.Text.Json.JsonElement>> Meta);

    private void Seed(Action<PmDbContext> seed)
    {
        // 先建 client 觸發 Migrate,再用 scope 塞資料
        _ = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        seed(db);
        db.SaveChanges();
    }

    [Fact]
    public async Task Tree_groups_hierarchy_counts_present_only_and_flags_multi()
    {
        Seed(db =>
        {
            var root = new LibraryRoot { Name = "r", AbsPath = "/tmp/r-" + Guid.NewGuid() };
            db.LibraryRoots.Add(root);
            db.SaveChanges();

            var character = new Tag { Name = "character", Kind = "general" };
            var hoshimachi = new Tag { Name = "hoshimachi_suisei", Kind = "character" };
            var copyA = new Tag { Name = "hololive", Kind = "copyright" };
            var copyB = new Tag { Name = "vtuber", Kind = "copyright" };
            var general = new Tag { Name = "1girl", Kind = "general" };
            var meta = new Tag { Name = "2024", Kind = "meta" };
            var lonely = new Tag { Name = "orphan", Kind = "general" };
            db.Tags.AddRange(character, hoshimachi, copyA, copyB, general, meta, lonely);
            db.SaveChanges();

            // 階層:character -> hoshimachi;且 hoshimachi 同時掛在 hololive 與 vtuber 下(>=2 parent => multi)
            db.TagRelations.AddRange(
                new TagRelation { ParentTagId = character.Id, ChildTagId = hoshimachi.Id },
                new TagRelation { ParentTagId = copyA.Id, ChildTagId = hoshimachi.Id },
                new TagRelation { ParentTagId = copyB.Id, ChildTagId = hoshimachi.Id });

            // 一張 present photo + 一張全 archived photo,皆掛 hoshimachi
            var pPresent = new Photo { FileHash = new string('a', 64) };
            var pArchived = new Photo { FileHash = new string('b', 64) };
            db.Photos.AddRange(pPresent, pArchived);
            db.SaveChanges();

            db.PhotoLocations.Add(new PhotoLocation { PhotoId = pPresent.Id, LibraryRootId = root.Id, RelPath = "a.png", Status = "present" });
            db.PhotoLocations.Add(new PhotoLocation { PhotoId = pArchived.Id, LibraryRootId = root.Id, RelPath = "b.png", Status = "archived" });

            db.PhotoTags.AddRange(
                new PhotoTag { PhotoId = pPresent.Id, TagId = hoshimachi.Id, Source = "wd14", Confidence = 0.9f },
                new PhotoTag { PhotoId = pArchived.Id, TagId = hoshimachi.Id, Source = "wd14", Confidence = 0.9f },
                new PhotoTag { PhotoId = pPresent.Id, TagId = general.Id, Source = "wd14", Confidence = 0.8f },
                new PhotoTag { PhotoId = pPresent.Id, TagId = meta.Id, Source = "path" });
        });

        var client = _factory.CreateClient();
        var t = await GetTree(client);

        // character 是頂層(無 parent 邊)且有子 => 出現在 tree
        var character = t!.Tree.FirstOrDefault(n => n.Name == "character");
        Assert.NotNull(character);
        var child = character!.Children!.Single(c => c.Name == "hoshimachi_suisei");

        // count = 直接擁有且有 present location 的 photo 數 = 1(archived 那張不算)
        Assert.Equal(1, child.Count);
        // hoshimachi 有 3 個 parent 邊 => multi
        Assert.True(child.Multi);

        // orphan:無 parent、無 child => rootless
        Assert.Contains(t.Rootless, n => n.Name == "orphan");
        Assert.DoesNotContain(t.Tree, n => n.Name == "orphan");

        // general / meta 清單(形狀為 [name, count])
        Assert.Contains(t.General, p => p[0].GetString() == "1girl" && p[1].GetInt32() == 1);
        Assert.Contains(t.Meta, p => p[0].GetString() == "2024" && p[1].GetInt32() == 1);
    }

    private async Task<TreeDto?> GetTree(HttpClient client)
    {
        var resp = await client.GetAsync("/api/tags/tree");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<TreeDto>();
    }
}
