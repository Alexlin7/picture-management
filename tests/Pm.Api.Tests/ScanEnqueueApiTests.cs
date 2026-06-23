using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Api.Tests;

// Slice 2:掃描排 tagging job 改可選。端點預設綁能力旗標(Inference:Enabled,測試環境關),
// 可由 ?enqueueTagging= 覆寫。用真正可解碼 PNG 才會觸發排 job 路徑;全程 Inference 關閉,不載模型。
public class ScanEnqueueApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-apidb-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-apiroot-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public ScanEnqueueApiTests()
    {
        Directory.CreateDirectory(_root);
        using (var img = new Image<Rgba32>(2, 2))
            img.SaveAsPng(Path.Combine(_root, "a.png"));   // 真 PNG → 可解碼 → 會走排 job 路徑

        var dbPath = _dbPath;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={dbPath};Foreign Keys=True",
                    ["Inference:Enabled"] = "false"
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
    private record ScanDto(int FilesSeen, int NewPhotos, int NewLocations, int JobsQueued);

    private async Task<long> CreateRootAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync("/api/roots", new { name = "test", absPath = _root });
        var root = await create.Content.ReadFromJsonAsync<RootCreated>();
        return root!.Id;
    }

    [Fact]
    public async Task Scan_with_inference_off_does_not_queue_jobs_by_default()
    {
        var client = _factory.CreateClient();
        var rootId = await CreateRootAsync(client);

        var scan = await client.PostAsync($"/api/roots/{rootId}/scan", null);
        var result = await scan.Content.ReadFromJsonAsync<ScanDto>();

        Assert.Equal(1, result!.NewPhotos);    // 身分照索引
        Assert.Equal(0, result.JobsQueued);    // 能力關 → 預設不排 job
    }

    [Fact]
    public async Task Scan_with_enqueueTagging_true_prequeues_jobs_even_when_inference_off()
    {
        var client = _factory.CreateClient();
        var rootId = await CreateRootAsync(client);

        var scan = await client.PostAsync($"/api/roots/{rootId}/scan?enqueueTagging=true", null);
        var result = await scan.Content.ReadFromJsonAsync<ScanDto>();

        Assert.Equal(1, result!.JobsQueued);   // 明示 → pre-queue(待之後啟用 worker 再消化)
    }
}
