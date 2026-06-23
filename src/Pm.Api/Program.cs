using Microsoft.EntityFrameworkCore;
using Pm.Api;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PmDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Pm")));

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConfiguration>().GetSection("Thumbnails").Get<ThumbnailOptions>()
        ?? new ThumbnailOptions());
builder.Services.AddScoped<IFileHasher, Sha256FileHasher>();
builder.Services.AddScoped<IImageMetadataReader, ExifImageMetadataReader>();
builder.Services.AddScoped<IThumbnailService, ThumbnailService>();
builder.Services.AddScoped<LibraryScanner>();
builder.Services.AddScoped<PathTagService>();
builder.Services.AddScoped<TagClosureService>();
builder.Services.AddScoped<PhotoQueryService>();
builder.Services.AddScoped<TagFacetService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<TaggingScheduler>();

// WD14 自動標籤(opt-in:Inference:Wd14:Enabled,預設關)。開啟才註冊推論工廠 + tagger + 背景 worker。
builder.Services.AddWd14Tagging(builder.Configuration);

var app = builder.Build();

// 啟動時確保 schema 存在(本機單檔,直接 Migrate)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
    db.Database.Migrate();
    // 開 WAL:API 請求與 WD14 背景 worker 同時寫入 pm.sqlite 時,讀寫不互卡(rollback journal 會)。
    // WAL 為持久設定(寫入 db header),設一次後所有連線生效。短暫寫鎖則靠 Microsoft.Data.Sqlite
    // 預設 command timeout(SQLITE_BUSY 自動重試)等待,而非立即「database is locked」。
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

// 由 .NET serve Angular 靜態檔(ng build 輸出至 wwwroot),同源、免 CORS
app.UseDefaultFiles();
app.UseStaticFiles();

// liveness:程序活著就好,不碰 DB
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// readiness:確認 DB 開得起來
app.MapGet("/health/db", async (PmDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect
        ? Results.Ok(new { db = "ok" })
        : Results.Json(new { db = "down" }, statusCode: 503);
});

app.MapGet("/api/roots", async (PmDbContext db) =>
    Results.Ok(await db.LibraryRoots.Select(r => new { r.Id, r.Name, r.AbsPath }).ToListAsync()));

app.MapPost("/api/roots", async (CreateRootDto dto, PmDbContext db) =>
{
    var root = new LibraryRoot { Name = dto.Name, AbsPath = dto.AbsPath };
    db.LibraryRoots.Add(root);
    await db.SaveChangesAsync();
    return Results.Created($"/api/roots/{root.Id}", new { root.Id, root.Name, root.AbsPath });
});

// enqueueTagging:是否為新可解碼圖排 WD14 job。未帶 query → 跟隨 WD14 能力旗標(Inference:Wd14:Enabled),
// 推論關時預設純索引、不堆死 job;明示 ?enqueueTagging=true 可在推論關時 pre-queue,?=false 強制只索引。
app.MapPost("/api/roots/{id:long}/scan", async (long id, bool? enqueueTagging, LibraryScanner scanner, IConfiguration config) =>
{
    var enqueue = enqueueTagging ?? config.GetValue<bool>("Inference:Wd14:Enabled");
    var result = await scanner.ScanRootAsync(id, enqueue);
    return Results.Ok(result);
});

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
});

app.MapGet("/api/roots/{id:long}/pending-segments", async (long id, PathTagService svc) =>
    Results.Ok(await svc.GetPendingSegmentsAsync(id)));

app.MapPost("/api/path-rules", async (PathRuleDto dto, PathTagService svc) =>
{
    await svc.ApplyRuleAsync(dto.RootId, dto.Segment, dto.Action, dto.TagName);
    return Results.Ok();
});

app.MapPost("/api/roots/{id:long}/apply-path-tags", async (long id, PathTagService svc) =>
    Results.Ok(new { rulesApplied = await svc.ApplyExistingRulesAsync(id) }));

app.MapPost("/api/search", async (SearchDto dto, PhotoQueryService svc) =>
    Results.Ok(await svc.SearchAsync(dto.All ?? [], dto.None ?? [], dto.AfterId, dto.PageSize ?? 200)));

app.MapGet("/api/photos/{id:long}/thumb", async (long id, PmDbContext db, IThumbnailService thumbs) =>
{
    var hash = await db.Photos.Where(p => p.Id == id).Select(p => p.FileHash).FirstOrDefaultAsync();
    if (hash is null) return Results.NotFound();
    var path = Path.GetFullPath(thumbs.PathFor(hash));   // 絕對路徑:走 PhysicalFile 而非 VirtualFile(wwwroot)
    return File.Exists(path) ? Results.File(path, "image/webp") : Results.NotFound();
});

app.MapGet("/api/photos/{id:long}", async (long id, PmDbContext db) =>
{
    var photo = await db.Photos.Include(p => p.Locations).Include(p => p.Tags)
        .FirstOrDefaultAsync(p => p.Id == id);
    if (photo is null) return Results.NotFound();

    var tagIds = photo.Tags.Select(t => t.TagId).ToList();
    var tags = await db.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync();
    var tagView = photo.Tags.Join(tags, pt => pt.TagId, t => t.Id,
        (pt, t) => new { id = t.Id, name = t.Name, kind = t.Kind, source = pt.Source, confidence = pt.Confidence });

    return Results.Ok(new
    {
        photo.Id,
        photo.FileHash,
        photo.Width,
        photo.Height,
        photo.Mime,
        photo.TakenAt,
        photo.CameraModel,
        locations = photo.Locations.Select(l => new { l.LibraryRootId, l.RelPath, l.Status }),
        tags = tagView
    });
});

app.MapGet("/api/saved-searches", async (PmDbContext db) =>
    Results.Ok(await db.SavedSearches.OrderByDescending(s => s.Id).ToListAsync()));

app.MapPost("/api/saved-searches", async (SavedSearchDto dto, PmDbContext db) =>
{
    var s = new SavedSearch { Name = dto.Name, QueryJson = dto.QueryJson };
    db.SavedSearches.Add(s);
    await db.SaveChangesAsync();
    return Results.Created($"/api/saved-searches/{s.Id}", new { s.Id });
});

app.MapDelete("/api/saved-searches/{id:long}", async (long id, PmDbContext db) =>
{
    var s = await db.SavedSearches.FindAsync(id);
    if (s is null) return Results.NotFound();
    db.SavedSearches.Remove(s);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

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
});

// reconcile:軟刪(把該 photo 所有 location 標 archived,保留 photo+tags)
app.MapPost("/api/photos/{id:long}/archive", async (long id, PmDbContext db) =>
{
    if (!await db.Photos.AnyAsync(p => p.Id == id)) return Results.NotFound();
    var locs = await db.PhotoLocations.Where(l => l.PhotoId == id).ToListAsync();
    foreach (var l in locs) l.Status = "archived";
    await db.SaveChangesAsync();
    return Results.Ok(new { archived = locs.Count });
});

// reconcile:硬刪 purge(cascade 連帶 location/photo_tag),僅明示端點才做
app.MapDelete("/api/photos/{id:long}", async (long id, PmDbContext db) =>
{
    var photo = await db.Photos.FindAsync(id);
    if (photo is null) return Results.NotFound();
    db.Photos.Remove(photo);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// 寫 manual tag(upsert tag,新增 photo_tag source='manual';已存在則 idempotent)
app.MapPost("/api/photos/{id:long}/tags", async (long id, ManualTagDto dto, PmDbContext db, TagService tags) =>
{
    if (!await db.Photos.AnyAsync(p => p.Id == id)) return Results.NotFound();
    if (TagService.Normalize(dto.Name).Length == 0)
        return Results.BadRequest(new { error = "標籤名不可為空白" });

    // 正規化 + 不分大小寫 upsert(blue/Blue 不會變兩個);全新名稱會進標籤庫。
    // 加 photo_tag 走與 wd14 worker 共用的 AttachTagAsync(idempotent)。
    var tag = await tags.UpsertByNameAsync(dto.Name, dto.Kind ?? "manual");
    if (await tags.AttachTagAsync(id, tag.Id, "manual", null)) await db.SaveChangesAsync();

    var pt = await db.PhotoTags.FirstAsync(x => x.PhotoId == id && x.TagId == tag.Id);
    return Results.Ok(new { id = tag.Id, name = tag.Name, kind = tag.Kind, source = pt.Source, confidence = pt.Confidence });
});

// 移除 photo_tag
app.MapDelete("/api/photos/{id:long}/tags/{tagId:long}", async (long id, long tagId, PmDbContext db) =>
{
    var pt = await db.PhotoTags.FirstOrDefaultAsync(x => x.PhotoId == id && x.TagId == tagId);
    if (pt is null) return Results.NotFound();
    db.PhotoTags.Remove(pt);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

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
});

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
});

// 標籤庫:列出 + 使用數(autocomplete 與管理頁共用);q 不分大小寫過濾
app.MapGet("/api/tags", async (string? q, int? limit, TagService tags) =>
    Results.Ok(await tags.ListAsync(q, Math.Clamp(limit ?? 50, 1, 500))));

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
});

// 編輯:改名 and/or 改 kind(任一可省);改名撞既有→合併。回 { merged }
app.MapPut("/api/tags/{id:long}", async (long id, UpdateTagDto dto, TagService tags) =>
{
    if (dto.Name is not null && TagService.Normalize(dto.Name).Length == 0)
        return Results.BadRequest(new { error = "標籤名不可為空白" });
    var (found, merged) = await tags.UpdateAsync(id, dto.Name, dto.Kind);
    return found ? Results.Ok(new { merged }) : Results.NotFound();
});

// 刪除 tag(連帶關聯)
app.MapDelete("/api/tags/{id:long}", async (long id, TagService tags) =>
    await tags.DeleteAsync(id) ? Results.NoContent() : Results.NotFound());

// 合併 id → targetId
app.MapPost("/api/tags/{id:long}/merge/{targetId:long}", async (long id, long targetId, TagService tags) =>
    await tags.MergeAsync(id, targetId) ? Results.Ok() : Results.NotFound());

// SPA fallback:前端路由不被 API 404 攔截
app.MapFallbackToFile("index.html");

app.Run();

public record CreateRootDto(string Name, string AbsPath);
public record PathRuleDto(long? RootId, string Segment, string Action, string? TagName);
public record SearchDto(string[]? All, string[]? None, long? AfterId, int? PageSize);
public record SavedSearchDto(string Name, string QueryJson);
public record ManualTagDto(string Name, string? Kind);
public record CreateTagDto(string Name, string? Kind);
public record UpdateTagDto(string? Name, string? Kind);

public partial class Program { }   // 供 WebApplicationFactory 測試引用
