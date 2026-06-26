using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Api;

public static class SavedSearchEndpoints
{
    public static void MapSavedSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/saved-searches", async (PmDbContext db) =>
            Results.Ok(await db.SavedSearches.OrderByDescending(s => s.Id).ToListAsync()))
            .WithTags("SavedSearches");

        app.MapPost("/api/saved-searches", async (SavedSearchDto dto, PmDbContext db) =>
        {
            var s = new SavedSearch { Name = dto.Name, QueryJson = dto.QueryJson };
            db.SavedSearches.Add(s);
            await db.SaveChangesAsync();
            return Results.Created($"/api/saved-searches/{s.Id}", new { s.Id });
        })
            .WithTags("SavedSearches");

        app.MapDelete("/api/saved-searches/{id:long}", async (long id, PmDbContext db) =>
        {
            var s = await db.SavedSearches.FindAsync(id);
            if (s is null) return Results.NotFound();
            db.SavedSearches.Remove(s);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
            .WithTags("SavedSearches");
    }
}

/// <summary>儲存搜尋請求:名稱 + 序列化為 JSON 的查詢條件。</summary>
public record SavedSearchDto(string Name, string QueryJson);
