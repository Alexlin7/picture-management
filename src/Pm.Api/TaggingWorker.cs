using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Ml;
using Pm.Scanner;

namespace Pm.Api;

public sealed class TaggingWorker(
    IServiceScopeFactory scopes, IWd14Tagger tagger, ILogger<TaggingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // 啟動先回收上次崩潰留下的孤兒 job(見 RecoverStuckJobsAsync)。
        using (var startScope = scopes.CreateScope())
        {
            var startDb = startScope.ServiceProvider.GetRequiredService<PmDbContext>();
            await RecoverStuckJobsAsync(startDb, ct);
        }

        while (!ct.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            var tagSvc = scope.ServiceProvider.GetRequiredService<TagService>();
            var processed = await ProcessNextAsync(db, tagSvc, ct);
            if (!processed) await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    // 啟動回收:程序若在「設 running 後、設 done/error 前」崩潰,job 會永遠卡在 "running"
    // (ProcessNextAsync 只撈 "pending")。啟動時把孤兒 "running" 重設回 "pending" 重排。
    // 單一 worker 前提下安全;未來多 consumer 需改用帶租約的 atomic claim。回收筆數。
    public async Task<int> RecoverStuckJobsAsync(PmDbContext db, CancellationToken ct = default)
        => await db.TaggingJobs
            .Where(j => j.State == "running")
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, "pending")
                .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);

    public async Task<bool> ProcessNextAsync(PmDbContext db, TagService tagSvc, CancellationToken ct)
    {
        var job = await db.TaggingJobs
            .Where(j => j.State == "pending")
            .OrderBy(j => j.PhotoId)   // SQLite 不支援 DateTimeOffset ORDER BY;photo id 遞增 ≈ FIFO
            .FirstOrDefaultAsync(ct);
        if (job is null) return false;

        job.State = "running";
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            var path = await ResolvePathAsync(db, job.PhotoId, ct)
                       ?? throw new FileNotFoundException($"photo {job.PhotoId} 無可用位置");

            // 走 TagService:正規化 + CI upsert(避免大小寫重複)+ 共用 AttachTag 路徑(與 manual 一致)。
            // 預載既有 tagId 一次,迴圈內不再逐 tag 查 photo_tag(消 N+1);photo_tag 最後一次 flush。
            var existing = await tagSvc.PhotoTagIdsAsync(job.PhotoId, ct);
            foreach (var (name, kind, conf) in await tagger.TagAsync(path, ct))
            {
                var tag = await tagSvc.UpsertByNameAsync(name, kind, ct);
                await tagSvc.AttachTagAsync(job.PhotoId, tag.Id, "wd14", conf, existing, ct);
            }

            job.State = "done";
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            job.Attempts++;
            job.State = "error";
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            log.LogWarning(ex, "tagging job {PhotoId} 失敗", job.PhotoId);
            return true;
        }
    }

    private static async Task<string?> ResolvePathAsync(PmDbContext db, long photoId, CancellationToken ct)
    {
        var loc = await db.PhotoLocations
            .Include(l => l.LibraryRoot)
            .Where(l => l.PhotoId == photoId && l.Status == "present")
            .FirstOrDefaultAsync(ct);
        return loc is null ? null : Path.Combine(loc.LibraryRoot.AbsPath, loc.RelPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
