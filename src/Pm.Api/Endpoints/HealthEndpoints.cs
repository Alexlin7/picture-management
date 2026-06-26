using Pm.Data;

namespace Pm.Api;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // liveness:程序活著就好,不碰 DB
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithTags("Health");

        // readiness:確認 DB 開得起來
        app.MapGet("/health/db", async (PmDbContext db) =>
        {
            var canConnect = await db.Database.CanConnectAsync();
            return canConnect
                ? Results.Ok(new { db = "ok" })
                : Results.Json(new { db = "down" }, statusCode: 503);
        })
            .WithTags("Health");
    }
}
