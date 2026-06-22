using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Ml;

namespace Pm.Api;

public sealed class TaggingWorker(
    IServiceScopeFactory scopes, IWd14Tagger tagger, ILogger<TaggingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            var processed = await ProcessNextAsync(db, ct);
            if (!processed) await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    public async Task<bool> ProcessNextAsync(PmDbContext db, CancellationToken ct)
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

            foreach (var (name, kind, conf) in await tagger.TagAsync(path, ct))
            {
                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
                if (tag is null)
                {
                    tag = new Tag { Name = name, Kind = kind };
                    db.Tags.Add(tag);
                    await db.SaveChangesAsync(ct);
                }
                if (!await db.PhotoTags.AnyAsync(pt => pt.PhotoId == job.PhotoId && pt.TagId == tag.Id, ct))
                    db.PhotoTags.Add(new PhotoTag { PhotoId = job.PhotoId, TagId = tag.Id, Source = "wd14", Confidence = conf });
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
