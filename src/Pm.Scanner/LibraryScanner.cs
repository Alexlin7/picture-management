using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Scanner;

public sealed class LibraryScanner(
    PmDbContext db, IFileHasher hasher, IImageMetadataReader meta, IThumbnailService thumbs,
    IImageReprocessor reprocessor)
{
    // 便利建構子:既有呼叫端(只給 db+hasher)沿用預設 reader/thumb/reprocessor。
    public LibraryScanner(PmDbContext db, IFileHasher hasher)
        : this(db, hasher, new ExifImageMetadataReader(), new ThumbnailService(new ThumbnailOptions()),
               new ImageReprocessor(new ExifImageMetadataReader(), new ThumbnailService(new ThumbnailOptions()))) { }

    // 便利建構子:提供自訂 meta/thumbs 但不傳 reprocessor(測試與舊呼叫端相容)→ reprocessor 沿用相同 meta/thumbs。
    public LibraryScanner(PmDbContext db, IFileHasher hasher, IImageMetadataReader meta, IThumbnailService thumbs)
        : this(db, hasher, meta, thumbs, new ImageReprocessor(meta, thumbs)) { }

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".avif", ".heic", ".heif", ".jfif"
    };

    private const int SlowPathBatchSize = 500;

    // enqueueTagging:是否為新可解碼圖排 WD14 tagging job。預設 true(行為不變);
    // 端點可傳 false 做「純索引」,或在推論關閉時明示 true 先 pre-queue(待之後啟用 worker 再消化)。
    public async Task<ScanResult> ScanRootAsync(long rootId, bool enqueueTagging = true, CancellationToken ct = default)
    {
        var root = await db.LibraryRoots.FindAsync([rootId], ct)
                   ?? throw new InvalidOperationException($"library_root {rootId} 不存在");

        int seen = 0, newPhotos = 0, newLocations = 0, skipped = 0, errors = 0;
        int thumbsGen = 0, jobsQueued = 0, markedMissing = 0, healed = 0;

        // 這輪走訪實際看到的位置(rel_path);對帳時 present 但不在此集合者 → missing。
        var seenPaths = new HashSet<string>();
        var locationsByPath = (await db.PhotoLocations
            .Where(l => l.LibraryRootId == rootId)
            .Select(l => new
            {
                Location = l,
                PhotoFileSize = l.Photo.FileSize,
                PhotoFileHash = l.Photo.FileHash,
                PhotoWidth = l.Photo.Width,
            })
            .ToListAsync(ct))
            .ToDictionary(l => l.Location.RelPath, StringComparer.Ordinal);

        var pending = new List<PendingScanFile>(SlowPathBatchSize);

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
                    if (locInfo.PhotoWidth is not null)
                    {
                        thumbsGen += await GenerateThumbIfMissingAsync(file, locInfo.PhotoFileHash, ct);
                    }
                    else
                    {
                        // 半殘圖(當初解碼失敗)→ 重新處理重建 metadata/縮圖,並(視 enqueueTagging)排 WD14。
                        // 以 AsNoTracking 讀取,避免 EF nav fixup 透過 Locations 集合連動已追蹤的 loc。
                        var rawPhoto = await db.Photos
                            .AsNoTracking()
                            .FirstOrDefaultAsync(p => p.Id == loc.PhotoId, ct);
                        if (rawPhoto is not null)
                        {
                            var r = await reprocessor.ReprocessAsync(rawPhoto, file, ct);
                            if (r.ThumbGenerated) thumbsGen++;
                            if (r.Decoded)
                            {
                                healed++;
                                // 解碼成功 → 掛進追蹤器,讓尾端 SaveChangesAsync 持久化解碼後的欄位。
                                db.Entry(rawPhoto).State = EntityState.Modified;
                                if (enqueueTagging)
                                {
                                    var job = await db.TaggingJobs.FindAsync([rawPhoto.Id], ct);
                                    if (job is null) db.TaggingJobs.Add(new TaggingJob { PhotoId = rawPhoto.Id });
                                    else { job.State = "pending"; job.Attempts = 0; job.UpdatedAt = DateTimeOffset.UtcNow; }
                                    jobsQueued++;
                                }
                            }
                            // 仍無法解碼 → rawPhoto 從未追蹤,不需清理。
                        }
                    }
                    skipped++;
                    continue;
                }

                var hash = await hasher.HashFileAsync(file, ct);
                pending.Add(new PendingScanFile(file, relPath, size, mtime, hash, loc));
                if (pending.Count >= SlowPathBatchSize)
                    await ProcessPendingAsync();
            }
            catch (IOException) { errors++; }
            catch (UnauthorizedAccessException) { errors++; }
        }

        await ProcessPendingAsync();

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        // 對帳:這輪沒看到、且仍標 present 的位置 → missing(軟刪,保留 photo+tags)。
        // 用開掃時已載入的 locationsByPath 在記憶體算出 missing 集合(已含該 root 全部 location),
        // 再以 location id 分塊更新。不可把 seenPaths 整包塞進單一 `RelPath NOT IN (@p1...@pN)`:
        // EF Core 對 SQLite 是逐元素參數,seenPaths 超過 SQLite 變數上限(32766)即 'too many SQL variables'。
        var missingIds = locationsByPath.Values
            .Where(x => x.Location.Status == "present" && !seenPaths.Contains(x.Location.RelPath))
            .Select(x => x.Location.Id)
            .ToList();

        foreach (var chunk in missingIds.Chunk(10_000))
            markedMissing += await db.PhotoLocations
                .Where(l => chunk.Contains(l.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, "missing"), ct);

        return new ScanResult(seen, newPhotos, newLocations, skipped, errors,
            thumbsGen, jobsQueued, markedMissing, healed);

        async Task ProcessPendingAsync()
        {
            if (pending.Count == 0) return;

            var batch = pending;
            pending = new List<PendingScanFile>(SlowPathBatchSize);

            var hashes = batch.Select(p => p.Hash).Distinct().ToList();
            var photosByHash = await db.Photos
                .AsNoTracking()
                .Where(p => hashes.Contains(p.FileHash))
                .ToDictionaryAsync(p => p.FileHash, StringComparer.Ordinal, ct);

            var newPhotosByHash = new Dictionary<string, NewPhotoWork>(StringComparer.Ordinal);
            var failedItems = new HashSet<PendingScanFile>();
            var batchTrackedEntities = new List<object>();
            var thumbWorkByHash = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in batch)
            {
                if (photosByHash.ContainsKey(item.Hash) || newPhotosByHash.ContainsKey(item.Hash))
                    continue;

                var photo = new Photo { FileHash = item.Hash, FileSize = item.Size };
                ImageMeta m;
                try
                {
                    m = meta.Read(item.File);
                }
                catch (IOException)
                {
                    errors++;
                    failedItems.Add(item);
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    errors++;
                    failedItems.Add(item);
                    continue;
                }

                photo.Width = m.Width;
                photo.Height = m.Height;
                photo.Mime = m.Mime;
                photo.TakenAt = m.TakenAt;
                photo.CameraModel = m.CameraModel;
                photo.GpsLat = m.GpsLat;
                photo.GpsLon = m.GpsLon;
                photo.Exif = m.ExifJson;

                db.Photos.Add(photo);
                batchTrackedEntities.Add(photo);
                newPhotosByHash.Add(item.Hash, new NewPhotoWork(photo, item.File, m.Width is not null));
                newPhotos++;
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            if (newPhotosByHash.Count > 0)
                await db.SaveChangesAsync(ct);   // 取得 photo.Id,但仍在同一交易內。

            foreach (var (hash, work) in newPhotosByHash)
            {
                photosByHash[hash] = work.Photo;

                // 只有可解碼的圖才產縮圖 + 排 WD14(壞圖/非圖留身分但不做)。
                // 縮圖本身在 DB transaction commit 後才產,失敗可由下次掃描補回。
                if (!work.CanDecode) continue;

                thumbWorkByHash.TryAdd(hash, work.File);

                // 縮圖屬索引一部分,照產;tagging job 才受 enqueueTagging 控制。
                if (!enqueueTagging) continue;

                var job = new TaggingJob { PhotoId = work.Photo.Id };
                db.TaggingJobs.Add(job);
                batchTrackedEntities.Add(job);
                jobsQueued++;
            }

            foreach (var item in batch)
            {
                if (failedItems.Contains(item)) continue;

                var photo = photosByHash[item.Hash];
                if (photo.Width is not null)
                    thumbWorkByHash.TryAdd(item.Hash, item.File);

                if (item.Location is null)
                {
                    var location = new PhotoLocation
                    {
                        PhotoId = photo.Id,
                        LibraryRootId = rootId,
                        RelPath = item.RelPath,
                        Status = "present",
                        Mtime = item.Mtime,
                        FirstSeenAt = DateTimeOffset.UtcNow,
                        LastSeenAt = DateTimeOffset.UtcNow,
                    };
                    db.PhotoLocations.Add(location);
                    batchTrackedEntities.Add(location);
                    newLocations++;
                }
                else
                {
                    // 既有位置但內容變了 → 指向(可能是新的)photo,更新中繼。
                    item.Location.PhotoId = photo.Id;
                    item.Location.Status = "present";
                    item.Location.Mtime = item.Mtime;
                    item.Location.LastSeenAt = DateTimeOffset.UtcNow;
                    batchTrackedEntities.Add(item.Location);
                }
            }

            if (db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            foreach (var (hash, file) in thumbWorkByHash)
                thumbsGen += await GenerateThumbIfMissingAsync(file, hash, ct);

            foreach (var entity in batchTrackedEntities)
                db.Entry(entity).State = EntityState.Detached;
        }
    }

    private async Task<int> GenerateThumbIfMissingAsync(string file, string hash, CancellationToken ct)
    {
        if (File.Exists(thumbs.PathFor(hash))) return 0;

        try
        {
            await thumbs.GenerateAsync(file, hash, ct);
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private sealed record PendingScanFile(
        string File,
        string RelPath,
        long Size,
        DateTimeOffset Mtime,
        string Hash,
        PhotoLocation? Location);

    private sealed record NewPhotoWork(Photo Photo, string File, bool CanDecode);
}
