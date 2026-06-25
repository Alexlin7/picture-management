using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Api.Tests;

public class ReconcileApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-recapi-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-recapiroot-{Guid.NewGuid():N}");
    private readonly string _thumbs = Path.Combine(Path.GetTempPath(), $"pm-recapithumbs-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public ReconcileApiTests()
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
    private record ScanStatusDto(string State, string? Error);

    [Fact]
    public async Task Missing_endpoint_lists_only_truly_gone_photos()
    {
        using (var img = new Image<Rgba32>(20, 20)) await img.SaveAsPngAsync(Path.Combine(_root, "gone.png"));
        using (var img = new Image<Rgba32>(30, 30)) await img.SaveAsPngAsync(Path.Combine(_root, "stay.png"));

        var client = _factory.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/roots", new { name = "t", absPath = _root }))
            .Content.ReadFromJsonAsync<RootCreated>();
        await client.PostAsync($"/api/roots/{created!.Id}/scan", null);
        await WaitForScanAsync(client, created.Id);

        File.Delete(Path.Combine(_root, "gone.png"));
        await client.PostAsync($"/api/roots/{created.Id}/scan", null);
        await WaitForScanAsync(client, created.Id);

        var resp = await client.GetAsync("/api/reconcile/missing");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();

        Assert.Contains("gone.png", body);
        Assert.DoesNotContain("stay.png", body);
    }

    private static async Task WaitForScanAsync(HttpClient client, long rootId)
    {
        for (var i = 0; i < 50; i++)
        {
            var status = await client.GetFromJsonAsync<ScanStatusDto>($"/api/roots/{rootId}/scan-status");
            if (status!.State == "completed") return;
            if (status.State == "error") throw new InvalidOperationException(status.Error);
            await Task.Delay(50);
        }

        throw new TimeoutException("scan did not finish");
    }
}
