using Pm.Scanner;

namespace Pm.Api;

public static class PathTagEndpoints
{
    public static void MapPathTagEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/roots/{id:long}/pending-segments", async (long id, PathTagService svc) =>
            Results.Ok(await svc.GetPendingSegmentsAsync(id)))
            .WithTags("PathTags");

        app.MapPost("/api/path-rules", async (PathRuleDto dto, PathTagService svc) =>
        {
            await svc.ApplyRuleAsync(dto.RootId, dto.Segment, dto.Action, dto.TagName, dto.Kind);
            return Results.Ok();
        })
            .WithTags("PathTags");

        app.MapPost("/api/roots/{id:long}/apply-path-tags", async (long id, PathTagService svc) =>
            Results.Ok(new { rulesApplied = await svc.ApplyExistingRulesAsync(id) }))
            .WithTags("PathTags");
    }
}

/// <summary>路徑段→tag 確認規則:指定根目錄、路徑段、動作(map/ignore/year)、對應標籤名與分類(kind)。</summary>
public record PathRuleDto(long? RootId, string Segment, string Action, string? TagName, string? Kind = null);
