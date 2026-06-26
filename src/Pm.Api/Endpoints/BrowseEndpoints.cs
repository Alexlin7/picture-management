using Pm.Scanner;

namespace Pm.Api;

public static class BrowseEndpoints
{
    public static void MapBrowseEndpoints(this IEndpointRouteBuilder app)
    {
        // 資料夾瀏覽維度:所有 root 摘要(頂層並列)
        app.MapGet("/api/folder-roots", async (FolderTreeService svc) =>
            Results.Ok(await svc.BuildRootsAsync()))
            .WithTags("Browse");

        // 某 root 的即時資料夾樹(遞迴 distinct present photo 計數)
        app.MapGet("/api/roots/{id:long}/folder-tree", async (long id, FolderTreeService svc) =>
            await svc.BuildTreeAsync(id) is { } tree ? Results.Ok(tree) : Results.NotFound())
            .WithTags("Browse");

        // 夾內可用 tag(自動完成用):範圍內 distinct present photo 的 tag 聚合
        app.MapGet("/api/browse/folder-tags", async (long rootId, string? path, FolderTreeService svc) =>
            Results.Ok(await svc.FolderTagsAsync(rootId, path)))
            .WithTags("Browse");
    }
}
