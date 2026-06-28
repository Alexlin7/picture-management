namespace Pm.Ml;

// 模型檔下載 helper —— backend/模型無關。WD14 與未來 CLIP 等都共用。
// 設計理由見 docs/design/2026-06-23-ml-layer-architecture-assessment.md §3。
public static class ModelArtifactDownloader
{
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
