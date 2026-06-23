using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Scanner;

public sealed class LibraryScanner(
    PmDbContext db, IFileHasher hasher, IImageMetadataReader meta, IThumbnailService thumbs)
{
    // 便利建構子:既有呼叫端(只給 db+hasher)沿用預設 reader/thumb。
    public LibraryScanner(PmDbContext db, IFileHasher hasher)
        : this(db, hasher, new ExifImageMetadataReader(), new ThumbnailService(new ThumbnailOptions())) { }

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".avif", ".jfif"
    };

    public async Task<ScanResult> ScanRootAsync(long rootId, CancellationToken ct = default)
    {
        var root = await db.LibraryRoots.FindAsync([rootId], ct)
                   ?? throw new InvalidOperationException($"library_root {rootId} 不存在");

        int seen = 0, newPhotos = 0, newLocations = 0, skipped = 0, errors = 0;
        int thumbsGen = 0, jobsQueued = 0, markedMissing = 0;

        // 這輪走訪實際看到的位置(rel_path);對帳時 present 但不在此集合者 → missing。
        var seenPaths = new HashSet<string>();
        var locationsByPath = (await db.PhotoLocations
            .Where(l => l.LibraryRootId == rootId)
            .Select(l => new { Location = l, PhotoFileSize = l.Photo.FileSize })
            .ToListAsync(ct))
            .ToDictionary(l => l.Location.RelPath, StringComparer.Ordinal);

        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(root.AbsPath, "*", opts))
        {
            ct.ThrowIfCancellationRequested();
            if (!ImageExts.Contains(Path.GetExtension(file))) continue;
            seen++;

            try
            {
                var info = new FileInfo(file);
                var relPath = Path.GetRelativePath(root.AbsPath, file).Replace('\\', '/');
                seenPaths.Add(relPath);
                var size = info.Length;
                var mtime = (DateTimeOffset)info.LastWriteTimeUtc;

                locationsByPath.TryGetValue(relPath, out var locInfo);
                var loc = locInfo?.Location;

                // 快路徑:同位置、present、size 與 mtime 都沒變(容 1 秒誤差,跨檔系統 mtime 精度不一)→ 不重算 hash。
                if (locInfo is not null
                    && loc is { Status: "present", Mtime: { } prevMtime }
                    && locInfo.PhotoFileSize == size
                    && (prevMtime - mtime).Duration() < TimeSpan.FromSeconds(1))
                {
                    loc.LastSeenAt = DateTimeOffset.UtcNow;
                    skipped++;
                    continue;
                }

                var hash = await hasher.HashFileAsync(file, ct);

                var photo = await db.Photos.FirstOrDefaultAsync(p => p.FileHash == hash, ct);
                if (photo is null)
                {
                    photo = new Photo { FileHash = hash, FileSize = size };

                    var m = meta.Read(file);
                    photo.Width = m.Width;
                    photo.Height = m.Height;
                    photo.Mime = m.Mime;
                    photo.TakenAt = m.TakenAt;
                    photo.CameraModel = m.CameraModel;
                    photo.GpsLat = m.GpsLat;
                    photo.GpsLon = m.GpsLon;
                    photo.Exif = m.ExifJson;

                    db.Photos.Add(photo);
                    await db.SaveChangesAsync(ct);   // 取得 photo.Id
                    newPhotos++;

                    // 只有可解碼的圖才產縮圖 + 排 WD14(壞圖/非圖留身分但不做)
                    if (m.Width is not null)
                    {
                        try
                        {
                            await thumbs.GenerateAsync(file, hash, ct);
                            thumbsGen++;
                        }
                        catch { /* 縮圖失敗不影響索引 */ }

                        db.TaggingJobs.Add(new TaggingJob { PhotoId = photo.Id });
                        await db.SaveChangesAsync(ct);
                        jobsQueued++;
                    }
                }

                if (loc is null)
                {
                    db.PhotoLocations.Add(new PhotoLocation
                    {
                        PhotoId = photo.Id,
                        LibraryRootId = rootId,
                        RelPath = relPath,
                        Status = "present",
                        Mtime = mtime,
                        FirstSeenAt = DateTimeOffset.UtcNow,
                        LastSeenAt = DateTimeOffset.UtcNow,
                    });
                    newLocations++;
                }
                else
                {
                    // 既有位置但內容變了 → 指向(可能是新的)photo,更新中繼。
                    loc.PhotoId = photo.Id;
                    loc.Status = "present";
                    loc.Mtime = mtime;
                    loc.LastSeenAt = DateTimeOffset.UtcNow;
                }

                await db.SaveChangesAsync(ct);
            }
            catch (IOException) { errors++; }
            catch (UnauthorizedAccessException) { errors++; }
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        // 對帳:這輪沒看到、且仍標 present 的位置 → missing(軟刪,保留 photo+tags)。
        // 以走訪集合(seenPaths)判斷,避免 SQLite 對 DateTimeOffset 比較無法翻譯的問題。
        markedMissing = await db.PhotoLocations
            .Where(l => l.LibraryRootId == rootId
                        && l.Status == "present"
                        && !seenPaths.Contains(l.RelPath))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, "missing"), ct);

        return new ScanResult(seen, newPhotos, newLocations, skipped, errors,
            thumbsGen, jobsQueued, markedMissing);
    }
}
