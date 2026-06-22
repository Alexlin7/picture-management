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
        if (!File.Exists(model)) await DownloadAsync(c => http.GetStreamAsync(opt.ModelOnnxUrl, c), model, ct);
        if (!File.Exists(tags)) await DownloadAsync(c => http.GetStreamAsync(opt.TagsCsvUrl, c), tags, ct);
        return (model, tags);
    }

    // 下到 dest + ".part" 暫存檔,複製完成後才 atomic rename 成 dest。
    // 中途失敗(斷線/逾時/被殺)清掉 .part,絕不在 dest 留半截壞檔 ——
    // 否則下次 File.Exists(dest)==true 會永遠把截斷檔當有效模型載入而每個 job 都炸。
    public static async Task DownloadAsync(
        Func<CancellationToken, Task<Stream>> openStream, string dest, CancellationToken ct)
    {
        var part = dest + ".part";
        try
        {
            await using (var s = await openStream(ct))
            await using (var f = File.Create(part))
                await s.CopyToAsync(f, ct);
            File.Move(part, dest, overwrite: true);
        }
        catch
        {
            if (File.Exists(part)) File.Delete(part);
            throw;
        }
    }
}
