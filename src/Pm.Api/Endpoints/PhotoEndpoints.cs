using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Scanner;

namespace Pm.Api;

public static class PhotoEndpoints
{
    public static void MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/photos/{id:long}/thumb", async (long id, PmDbContext db, IThumbnailService thumbs) =>
        {
            var hash = await db.Photos.Where(p => p.Id == id).Select(p => p.FileHash).FirstOrDefaultAsync();
            if (hash is null) return Results.NotFound();
            var path = Path.GetFullPath(thumbs.PathFor(hash));   // 絕對路徑:走 PhysicalFile 而非 VirtualFile(wwwroot)
            return await OpenThumbAsync(path);
        })
            .WithTags("Photos");

        app.MapGet("/api/photos/{id:long}", async (long id, PmDbContext db) =>
        {
            var photo = await db.Photos.Include(p => p.Locations).Include(p => p.Tags)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null) return Results.NotFound();

            var tagIds = photo.Tags.Select(t => t.TagId).ToList();
            var tags = await db.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync();
            var tagView = photo.Tags.Join(tags, pt => pt.TagId, t => t.Id,
                (pt, t) => new { id = t.Id, name = t.Name, kind = t.Kind, source = pt.Source, confidence = pt.Confidence });

            return Results.Ok(new
            {
                photo.Id,
                photo.FileHash,
                photo.Width,
                photo.Height,
                photo.Mime,
                photo.TakenAt,
                photo.CameraModel,
                locations = photo.Locations.Select(l => new { l.LibraryRootId, l.RelPath, l.Status }),
                tags = tagView
            });
        })
            .WithTags("Photos");

        // reconcile:軟刪(把該 photo 所有 location 標 archived,保留 photo+tags)
        app.MapPost("/api/photos/{id:long}/archive", async (long id, PmDbContext db) =>
        {
            if (!await db.Photos.AnyAsync(p => p.Id == id)) return Results.NotFound();
            var locs = await db.PhotoLocations.Where(l => l.PhotoId == id).ToListAsync();
            foreach (var l in locs) l.Status = "archived";
            await db.SaveChangesAsync();
            return Results.Ok(new { archived = locs.Count });
        })
            .WithTags("Photos");

        // reconcile:硬刪 purge(cascade 連帶 location/photo_tag),僅明示端點才做
        app.MapDelete("/api/photos/{id:long}", async (long id, PmDbContext db) =>
        {
            var photo = await db.Photos.FindAsync(id);
            if (photo is null) return Results.NotFound();
            db.Photos.Remove(photo);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
            .WithTags("Photos");

        // 寫 manual tag(upsert tag,新增 photo_tag source='manual';已存在則 idempotent)
        app.MapPost("/api/photos/{id:long}/tags", async (long id, ManualTagDto dto, PmDbContext db, TagService tags) =>
        {
            if (!await db.Photos.AnyAsync(p => p.Id == id)) return Results.NotFound();
            if (TagService.Normalize(dto.Name).Length == 0)
                return Results.BadRequest(new { error = "標籤名不可為空白" });

            // 正規化 + 不分大小寫 upsert(blue/Blue 不會變兩個);全新名稱會進標籤庫。
            // 加 photo_tag 走與 wd14 worker 共用的 AttachTagAsync(idempotent)。
            var tag = await tags.UpsertByNameAsync(dto.Name, dto.Kind ?? "manual");
            if (await tags.AttachTagAsync(id, tag.Id, "manual", null)) await db.SaveChangesAsync();

            var pt = await db.PhotoTags.FirstAsync(x => x.PhotoId == id && x.TagId == tag.Id);
            return Results.Ok(new { id = tag.Id, name = tag.Name, kind = tag.Kind, source = pt.Source, confidence = pt.Confidence });
        })
            .WithTags("Photos");

        // 移除 photo_tag
        app.MapDelete("/api/photos/{id:long}/tags/{tagId:long}", async (long id, long tagId, PmDbContext db) =>
        {
            var pt = await db.PhotoTags.FirstOrDefaultAsync(x => x.PhotoId == id && x.TagId == tagId);
            if (pt is null) return Results.NotFound();
            db.PhotoTags.Remove(pt);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
            .WithTags("Photos");

        // 單張重新處理:重新解碼 → 補 metadata + 強制重產縮圖 → refresh WD14(清舊 wd14 + 重排)。
        app.MapPost("/api/photos/{id:long}/reprocess", async (
            long id, PmDbContext db, IImageReprocessor reprocessor, TaggingScheduler scheduler) =>
        {
            var photo = await db.Photos.Include(p => p.Locations).FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null) return Results.NotFound();

            var loc = photo.Locations.FirstOrDefault(l => l.Status == "present");
            if (loc is null) return Results.Json(new { error = "no readable location" }, statusCode: 409);

            var root = await db.LibraryRoots.FindAsync(loc.LibraryRootId);
            if (root is null) return Results.Json(new { error = "root missing" }, statusCode: 409);
            var absPath = Path.GetFullPath(Path.Combine(root.AbsPath, loc.RelPath.Replace('/', Path.DirectorySeparatorChar)));

            var result = await reprocessor.ReprocessAsync(photo, absPath);
            await db.SaveChangesAsync();

            if (result.Decoded)
                await scheduler.ScheduleAsync("refresh", new RequeueScopeDto(PhotoIds: [id]));

            return Results.Ok(new { decoded = result.Decoded, thumbGenerated = result.ThumbGenerated });
        })
            .WithTags("Photos");
    }

    private static async Task<IResult> OpenThumbAsync(string path)
    {
        if (!File.Exists(path)) return Results.NotFound();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                return Results.Stream(stream, "image/webp");
            }
            catch (IOException) when (attempt == 0)
            {
                await Task.Delay(80);
            }
            catch (IOException ex)
            {
                return Results.Json(new { error = "thumbnail temporarily unavailable", detail = ex.Message }, statusCode: 503);
            }
        }

        return Results.Json(new { error = "thumbnail temporarily unavailable" }, statusCode: 503);
    }
}

/// <summary>手動加標請求:標籤名稱與可選分類(kind)。</summary>
public record ManualTagDto(string Name, string? Kind);
