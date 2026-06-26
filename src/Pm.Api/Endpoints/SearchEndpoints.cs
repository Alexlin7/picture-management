using Pm.Scanner;

namespace Pm.Api;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/search", async (SearchDto dto, PhotoQueryService svc) =>
            Results.Ok(await svc.SearchAsync(dto.All ?? [], dto.None ?? [], dto.AfterId, dto.PageSize ?? 200, dto.RootId, dto.PathPrefix)))
            .WithTags("Search");

        app.MapPost("/api/search/count", async (SearchDto dto, PhotoQueryService svc) =>
            Results.Ok(new { total = await svc.CountAsync(dto.All ?? [], dto.None ?? [], dto.RootId, dto.PathPrefix) }))
            .WithTags("Search");
    }
}

/// <summary>布林查詢請求:all 取交集、none 排除、keyset 分頁(AfterId + PageSize);RootId+PathPrefix 限縮到某資料夾子樹(瀏覽維度)。</summary>
public record SearchDto(string[]? All, string[]? None, long? AfterId, int? PageSize, long? RootId, string? PathPrefix);
