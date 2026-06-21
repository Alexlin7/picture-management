using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PmDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Pm")));

builder.Services.AddScoped<IFileHasher, Sha256FileHasher>();
builder.Services.AddScoped<LibraryScanner>();

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

app.Run();

public record CreateRootDto(string Name, string AbsPath);

public partial class Program { }   // 供 WebApplicationFactory 測試引用
