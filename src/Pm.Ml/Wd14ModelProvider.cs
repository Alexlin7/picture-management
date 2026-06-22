namespace Pm.Ml;

public static class Wd14ModelProvider
{
    // 缺檔則從 HF 下載;回 (modelPath, tagsCsvPath)。
    public static async Task<(string Model, string Tags)> EnsureAsync(Wd14Options opt, CancellationToken ct = default)
    {
        Directory.CreateDirectory(opt.ModelDir);
        var model = Path.Combine(opt.ModelDir, "model.onnx");
        var tags = Path.Combine(opt.ModelDir, "selected_tags.csv");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (!File.Exists(model)) await DownloadAsync(http, opt.ModelOnnxUrl, model, ct);
        if (!File.Exists(tags)) await DownloadAsync(http, opt.TagsCsvUrl, tags, ct);
        return (model, tags);
    }

    private static async Task DownloadAsync(HttpClient http, string url, string dest, CancellationToken ct)
    {
        await using var s = await http.GetStreamAsync(url, ct);
        await using var f = File.Create(dest);
        await s.CopyToAsync(f, ct);
    }
}
