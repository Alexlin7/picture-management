using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Api;

public static class RootEndpoints
{
    public static void MapRootEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/roots", async (PmDbContext db) =>
            Results.Ok(await db.LibraryRoots.Select(r => new { r.Id, r.Name, r.AbsPath }).ToListAsync()))
            .WithTags("Roots");

        app.MapPost("/api/roots", async (CreateRootDto dto, PmDbContext db) =>
        {
            var root = new LibraryRoot { Name = dto.Name, AbsPath = dto.AbsPath };
            db.LibraryRoots.Add(root);
            await db.SaveChangesAsync();
            return Results.Created($"/api/roots/{root.Id}", new { root.Id, root.Name, root.AbsPath });
        })
            .WithTags("Roots");

        // enqueueTagging:是否為新可解碼圖排 WD14 job。未帶 query → 跟隨 WD14 能力旗標(Inference:Wd14:Enabled),
        // 推論關時預設純索引、不堆死 job;明示 ?enqueueTagging=true 可在推論關時 pre-queue,?=false 強制只索引。
        app.MapPost("/api/roots/{id:long}/scan", async (
            long id,
            bool? enqueueTagging,
            PmDbContext db,
            RootScanCoordinator scans) =>
        {
            if (!await db.LibraryRoots.AnyAsync(r => r.Id == id)) return Results.NotFound();

            if (!scans.TryStart(id, enqueueTagging, out var status))
                return Results.Conflict(status);

            return Results.Accepted($"/api/roots/{id}/scan-status", status);
        })
            .WithTags("Roots");

        app.MapGet("/api/roots/{id:long}/scan-status", async (long id, PmDbContext db, RootScanCoordinator scans) =>
        {
            if (!await db.LibraryRoots.AnyAsync(r => r.Id == id)) return Results.NotFound();
            return Results.Ok(scans.GetStatus(id));
        })
            .WithTags("Roots");
    }
}

/// <summary>建立圖庫來源的請求:顯示名 + 絕對路徑。</summary>
public record CreateRootDto(string Name, string AbsPath);
