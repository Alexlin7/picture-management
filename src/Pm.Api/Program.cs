using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

// 啟動時確保 schema 存在(本機單檔,直接 Migrate)
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<PmDbContext>().Database.Migrate();
}

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

app.MapGet("/", () => "Picture Management API");

app.MapPost("/api/roots", async (CreateRootDto dto, PmDbContext db) =>
{
    var root = new LibraryRoot { Name = dto.Name, AbsPath = dto.AbsPath };
    db.LibraryRoots.Add(root);
    await db.SaveChangesAsync();
    return Results.Created($"/api/roots/{root.Id}", new { root.Id, root.Name, root.AbsPath });
});

app.MapPost("/api/roots/{id:long}/scan", async (long id, LibraryScanner scanner) =>
{
    var result = await scanner.ScanRootAsync(id);
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
    var path = thumbs.PathFor(hash);
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

app.Run();

public record CreateRootDto(string Name, string AbsPath);
public record PathRuleDto(long? RootId, string Segment, string Action, string? TagName);
public record SearchDto(string[]? All, string[]? None, long? AfterId, int? PageSize);

public partial class Program { }   // 供 WebApplicationFactory 測試引用
