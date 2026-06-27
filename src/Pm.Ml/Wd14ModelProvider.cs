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
        if (!File.Exists(model))
            await ModelArtifactDownloader.DownloadAsync(c => http.GetStreamAsync(opt.ModelOnnxUrl, c), model, ct);
        if (!File.Exists(tags))
            await ModelArtifactDownloader.DownloadAsync(c => http.GetStreamAsync(opt.TagsCsvUrl, c), tags, ct);
        return (model, tags);
    }
}
