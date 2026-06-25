using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pm.Data;
using Pm.Scanner;
using Xunit;

namespace Pm.Api.Tests;

public class CopyrightAxisApiTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pm-caxapi-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public CopyrightAxisApiTests()
    {
        var db = _db;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={db};Foreign Keys=True"
                })));
    }

    [Fact]
    public async Task Rebuild_creates_copyright_tags_and_edges_idempotently()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<TagService>();
            await tags.UpsertByNameAsync("aris_(blue_archive)", "character");
            await tags.UpsertByNameAsync("hoshino_(blue_archive)", "character");
            await tags.UpsertByNameAsync("long_hair", "general");   // 不該長出邊
        }
        var client = _factory.CreateClient();

        var res = await client.PostAsync("/api/maintenance/copyright-axis/rebuild", null);
        var body = await res.Content.ReadFromJsonAsync<Rebuild>();

        Assert.NotNull(body);
        Assert.Equal(2, body!.EdgesCreated);     // 兩個角色各一條邊
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            Assert.True(ctx.Tags.Any(t => t.Name == "blue_archive" && t.Kind == "copyright"));
            Assert.Equal(2, ctx.TagRelations.Count());
        }

        // 冪等:再跑一次不新增
        var res2 = await client.PostAsync("/api/maintenance/copyright-axis/rebuild", null);
        var body2 = await res2.Content.ReadFromJsonAsync<Rebuild>();
        Assert.Equal(0, body2!.EdgesCreated);
    }

    [Fact]
    public async Task Search_by_copyright_matches_child_character_photos()
    {
        long photoId;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            var tags = scope.ServiceProvider.GetRequiredService<TagService>();
            var axis = scope.ServiceProvider.GetRequiredService<CopyrightAxisService>();

            var root = new Pm.Data.Entities.LibraryRoot { Name = "r", AbsPath = @"C:\x" };
            ctx.LibraryRoots.Add(root);
            await ctx.SaveChangesAsync();

            var photo = new Pm.Data.Entities.Photo { FileHash = new string('b', 64), ImportedAt = DateTimeOffset.UtcNow };
            ctx.Photos.Add(photo);
            await ctx.SaveChangesAsync();
            photoId = photo.Id;

            ctx.PhotoLocations.Add(new Pm.Data.Entities.PhotoLocation
            {
                PhotoId = photo.Id,
                LibraryRootId = root.Id,
                RelPath = "a.png",
                Status = "present",
                FirstSeenAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow
            });

            var character = await tags.UpsertByNameAsync("aris_(blue_archive)", "character");
            ctx.PhotoTags.Add(new Pm.Data.Entities.PhotoTag { PhotoId = photo.Id, TagId = character.Id, Source = "wd14" });
            await ctx.SaveChangesAsync();

            await axis.SeedFromCharacterAsync(character);   // 建 blue_archive + 邊
        }

        var client = _factory.CreateClient();

        // 搜 "blue_archive"(copyright 父標),應命中只掛子角色標的 photo(closure 展開)。
        var resp = await client.PostAsJsonAsync("/api/search", new { all = new[] { "blue_archive" } });
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadFromJsonAsync<SearchPage>();

        Assert.NotNull(page);
        Assert.Contains(page!.Items, item => item.Id == photoId);
    }

    private sealed record SearchItem(long Id, string FileHash);
    private sealed record SearchPage(List<SearchItem> Items, long? NextCursor);
    private sealed record Rebuild(int Scanned, int EdgesCreated);

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
