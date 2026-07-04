using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Pm.Api.Tests;

// POST /api/roots 的守備:重複 abs_path 要回 409 Conflict(而非 unique constraint 冒成未處理 500)。
public class RootCreateTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-apidb-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public RootCreateTests()
    {
        var dbPath = _dbPath;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={dbPath};Foreign Keys=True"
                })));
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task Create_duplicate_abs_path_returns_409_not_500()
    {
        var client = _factory.CreateClient();
        var absPath = Path.Combine(Path.GetTempPath(), $"pm-dup-{Guid.NewGuid():N}");

        var first = await client.PostAsJsonAsync("/api/roots", new { name = "one", absPath });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/roots", new { name = "two", absPath });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
