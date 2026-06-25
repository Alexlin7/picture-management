using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Api.Tests;

public class OrphanCleanupApiTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pm-orphan-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public OrphanCleanupApiTests()
    {
        var db = _db;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={db};Foreign Keys=True"
                })));
    }

    // 寫一筆無 location 的孤兒 photo,回其 id。
    private async Task<long> SeedOrphanAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var p = new Photo { FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ImportedAt = DateTimeOffset.UtcNow };
        ctx.Photos.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    // 寫一筆有 present location 的正常 photo,回其 id。
    private async Task<long> SeedWithLocationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var root = new LibraryRoot { Name = "r", AbsPath = @"C:\x" };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        var p = new Photo { FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ImportedAt = DateTimeOffset.UtcNow };
        ctx.Photos.Add(p);
        await ctx.SaveChangesAsync();
        ctx.PhotoLocations.Add(new PhotoLocation { PhotoId = p.Id, LibraryRootId = root.Id, RelPath = "a.png", Status = "present", FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task Preview_lists_only_orphans()
    {
        var orphanId = await SeedOrphanAsync();
        await SeedWithLocationAsync();   // 不該出現
        var client = _factory.CreateClient();

        var res = await client.GetFromJsonAsync<OrphanPreview>("/api/maintenance/orphan-photos");

        Assert.NotNull(res);
        Assert.Equal(1, res!.Count);
        Assert.Equal(new[] { orphanId }, res.Ids);
    }

    private sealed record OrphanPreview(int Count, long[] Ids);

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
