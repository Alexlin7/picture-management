using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Scanner;

namespace Pm.Api;

public static class MaintenanceEndpoints
{
    public static void MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        // 維護:孤兒 photo(零 location,async scan 舊 bug 殘留)預覽 —— 先看再刪。
        app.MapGet("/api/maintenance/orphan-photos", async (PmDbContext db) =>
        {
            var ids = await db.Photos.Where(p => !p.Locations.Any()).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync();
            return Results.Ok(new { count = ids.Count, ids });
        })
            .WithTags("Maintenance");

        // 維護:硬刪孤兒 photo —— DB FK cascade 帶走 photo_tag/tagging_job(FK 全 app 強制開,見
        // SqliteSetup.BuildConnectionString);location 不必處理(孤兒恆零 location)。縮圖另刪(先取 hash 再刪 DB)。
        app.MapDelete("/api/maintenance/orphan-photos", async (PmDbContext db, IThumbnailService thumbs) =>
        {
            var orphans = await db.Photos.Where(p => !p.Locations.Any()).OrderBy(p => p.Id).ToListAsync();
            var hashes = orphans.Select(p => p.FileHash).ToList();   // RemoveRange 前先收 hash
            db.Photos.RemoveRange(orphans);
            await db.SaveChangesAsync();

            var thumbsDeleted = 0;
            foreach (var hash in hashes)
            {
                var path = thumbs.PathFor(hash);
                if (File.Exists(path)) { File.Delete(path); thumbsDeleted++; }
            }
            return Results.Ok(new { purged = orphans.Count, thumbsDeleted });
        })
            .WithTags("Maintenance");

        // 維護:對所有現有 character tag 補拆作品 + 寫 tag_relation 邊(冪等,可重跑)。
        app.MapPost("/api/maintenance/copyright-axis/rebuild", async (PmDbContext db, CopyrightAxisService axis) =>
        {
            var characters = await db.Tags.Where(t => t.Kind == "character").ToListAsync();
            var edgesCreated = 0;
            foreach (var c in characters)
                if (await axis.SeedFromCharacterAsync(c)) edgesCreated++;
            return Results.Ok(new { scanned = characters.Count, edgesCreated });
        })
            .WithTags("Maintenance");
    }
}
