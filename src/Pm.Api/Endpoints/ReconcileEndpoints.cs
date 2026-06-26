using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Api;

public static class ReconcileEndpoints
{
    public static void MapReconcileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/reconcile/missing", async (PmDbContext db) =>
        {
            var gone = await db.Photos
                .Where(p => p.Locations.Any() && p.Locations.All(l => l.Status != "present"))
                .Select(p => new
                {
                    id = p.Id,
                    fileHash = p.FileHash,
                    paths = p.Locations.Select(l => l.RelPath).ToList()
                })
                .ToListAsync();
            return Results.Ok(gone);
        })
            .WithTags("Reconcile");
    }
}
