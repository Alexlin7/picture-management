using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Pm.Api.Tests;

public class RootScanTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-apidb-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-apiroot-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public RootScanTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "a.png"), "alpha");
        File.WriteAllText(Path.Combine(_root, "b.png"), "beta");

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
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private record RootCreated(long Id, string Name, string AbsPath);
    private record ScanDto(int FilesSeen, int NewPhotos, int NewLocations, int SkippedUnchanged, int Errors);
    private record ScanStatusDto(string State, ScanDto? Result, string? Error);

    [Fact]
    public async Task Create_root_then_scan_indexes_files()
    {
        var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/roots", new { name = "test", absPath = _root });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var root = await create.Content.ReadFromJsonAsync<RootCreated>();
        Assert.NotNull(root);

        var scan = await client.PostAsync($"/api/roots/{root!.Id}/scan", null);
        Assert.Equal(HttpStatusCode.Accepted, scan.StatusCode);
        var status = await WaitForScanAsync(client, root.Id);

        Assert.Equal("completed", status.State);
        Assert.Equal(2, status.Result!.FilesSeen);
        Assert.Equal(2, status.Result.NewPhotos);
        Assert.Equal(2, status.Result.NewLocations);
    }

    private static async Task<ScanStatusDto> WaitForScanAsync(HttpClient client, long rootId)
    {
        for (var i = 0; i < 50; i++)
        {
            var status = await client.GetFromJsonAsync<ScanStatusDto>($"/api/roots/{rootId}/scan-status");
            if (status!.State != "running") return status;
            await Task.Delay(50);
        }

        throw new TimeoutException("scan did not finish");
    }
}
