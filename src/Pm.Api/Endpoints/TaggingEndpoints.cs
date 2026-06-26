using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Api;

public static class TaggingEndpoints
{
    public static void MapTaggingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/photos/{id:long}/retag", async (long id, string? mode, TaggingScheduler scheduler) =>
        {
            try
            {
                var result = await scheduler.ScheduleAsync(mode ?? "retry", new RequeueScopeDto(PhotoIds: [id]));
                return result.Matched == 0 ? Results.NotFound() : Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
            .WithTags("Tagging");

        app.MapPost("/api/tag/requeue", async (RequeueRequestDto dto, TaggingScheduler scheduler) =>
        {
            try
            {
                return Results.Ok(await scheduler.ScheduleAsync(dto.Mode, dto.Scope));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
            .WithTags("Tagging");

        app.MapGet("/api/tagging/stats", async (PmDbContext db) =>
            Results.Ok(new
            {
                pending = await db.TaggingJobs.CountAsync(j => j.State == "pending"),
                error = await db.TaggingJobs.CountAsync(j => j.State == "error"),
                running = await db.TaggingJobs.CountAsync(j => j.State == "running"),
            }))
            .WithTags("Tagging");
    }
}
