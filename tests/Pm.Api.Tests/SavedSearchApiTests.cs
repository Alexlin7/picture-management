using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Pm.Api.Tests;

public class SavedSearchApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-ssapi-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public SavedSearchApiTests()
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

    private record Created(long Id);

    [Fact]
    public async Task Create_list_delete_saved_search()
    {
        var client = _factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/saved-searches",
            new { name = "可能是個人照片", queryJson = "{\"all\":[],\"hasExif\":true}" }))
            .Content.ReadFromJsonAsync<Created>();
        Assert.NotNull(created);

        var list = await client.GetStringAsync("/api/saved-searches");
        Assert.Contains("可能是個人照片", list);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/saved-searches/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/saved-searches/{created.Id}")).StatusCode);
    }
}
