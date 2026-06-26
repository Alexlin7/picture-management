using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Scanner;

namespace Pm.Api;

public static class TagEndpoints
{
    public static void MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        // 側欄 facet 樹(餵 FacetSidebar)
        app.MapGet("/api/tags/tree", async (TagFacetService svc) =>
        {
            var f = await svc.BuildAsync();

            // FacetNode → 純物件;children 省略則 null(前端優雅隱藏)
            static object MapNode(FacetNode n) => new
            {
                name = n.Name,
                kind = n.Kind,
                count = n.Count,
                multi = n.Multi,
                children = n.Children?.Select(MapNode).ToList()
            };

            return Results.Ok(new
            {
                tree = f.Tree.Select(MapNode).ToList(),
                rootless = f.Rootless.Select(MapNode).ToList(),
                general = f.General.Select(p => new object[] { p.Name, p.Count }).ToList(),
                meta = f.Meta.Select(p => new object[] { p.Name, p.Count }).ToList()
            });
        })
            .WithTags("Tags");

        // 標籤庫:列出 + 使用數(autocomplete 與管理頁共用);q 不分大小寫過濾
        app.MapGet("/api/tags", async (string? q, int? limit, TagService tags) =>
            Results.Ok(await tags.ListAsync(q, Math.Clamp(limit ?? 50, 1, 500))))
            .WithTags("Tags");

        // 建純標籤(不掛圖):name + kind(預設 manual)。撞既有(CI)回 200 + existed:true;否則 201。
        app.MapPost("/api/tags", async (CreateTagDto dto, TagService tags, PmDbContext db) =>
        {
            var name = TagService.Normalize(dto.Name);
            if (name.Length == 0) return Results.BadRequest(new { error = "標籤名不可為空白" });
            var ci = name.ToLowerInvariant();
            var existing = await db.Tags.FirstOrDefaultAsync(t => t.NameCi == ci);
            if (existing is not null)
                return Results.Ok(new { id = existing.Id, name = existing.Name, kind = existing.Kind, existed = true });
            var tag = await tags.UpsertByNameAsync(name, dto.Kind ?? "manual");
            return Results.Created($"/api/tags/{tag.Id}", new { id = tag.Id, name = tag.Name, kind = tag.Kind, existed = false });
        })
            .WithTags("Tags");

        // 編輯:改名 and/or 改 kind(任一可省);改名撞既有→合併。回 { merged }
        app.MapPut("/api/tags/{id:long}", async (long id, UpdateTagDto dto, TagService tags) =>
        {
            if (dto.Name is not null && TagService.Normalize(dto.Name).Length == 0)
                return Results.BadRequest(new { error = "標籤名不可為空白" });
            var (found, merged) = await tags.UpdateAsync(id, dto.Name, dto.Kind);
            return found ? Results.Ok(new { merged }) : Results.NotFound();
        })
            .WithTags("Tags");

        // 刪除 tag(連帶關聯)
        app.MapDelete("/api/tags/{id:long}", async (long id, TagService tags) =>
            await tags.DeleteAsync(id) ? Results.NoContent() : Results.NotFound())
            .WithTags("Tags");

        // 合併 id → targetId
        app.MapPost("/api/tags/{id:long}/merge/{targetId:long}", async (long id, long targetId, TagService tags) =>
            await tags.MergeAsync(id, targetId) ? Results.Ok() : Results.NotFound())
            .WithTags("Tags");
    }
}

/// <summary>建立純標籤(不掛圖)請求:名稱與可選分類(kind,預設 manual)。</summary>
public record CreateTagDto(string Name, string? Kind);
/// <summary>更新標籤請求:可選改名與/或改 kind,任一可省略。</summary>
public record UpdateTagDto(string? Name, string? Kind);
