# 單張重新處理 + 前端 RWD 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 一條 branch 內完成兩件事:(A) 半殘圖可重新處理(掃描自動痊癒 + Inspector 單張手動);(B) 前端桌面縮放韌性(自刻瀑布流 + 可收合側欄 + 連續自適應欄數)。

**Architecture:** Phase A 核心是 `ImageReprocessor`(解碼→補 metadata→強制重產縮圖,改 tracked `Photo` 由呼叫端 save),掃描快路徑與 `POST /api/photos/{id}/reprocess` 共用;re-tag 各接既有 `TaggingScheduler`。Phase B 把寫死的 `column-count` / `grid-template-columns` 換成純函式 `computeMasonryLayout` + `<app-masonry>` 元件,側欄寬由 `useStageWidth` signal 驅動收合。

**Tech Stack:** .NET 10 / EF Core + SQLite / xUnit(後端);Angular standalone + Tailwind v4 / Vitest + TestBed / Playwright e2e(前端)。

## Global Constraints

- 鐵則 1:絕不改/搬/改名原圖、不寫回 metadata;只讀解碼。不靠改 mtime 逼慢路徑。
- 鐵則 2:`file_hash` 即身分;reprocess 不動 hash、不 rehash。
- 鐵則 5:只 refresh `wd14` tag;`manual`/`path` tag 不動。
- 鐵則 10:不逐表手刪;不涉硬刪。
- 設計來源:`docs/superpowers/specs/2026-06-26-photo-reprocess-and-scan-heal-design.md`、`docs/superpowers/specs/2026-06-26-frontend-rwd-design.md`。
- 前端樣式:元件 `.css` 隔離編譯,**不得** `@apply`/`@tailwind`/`@reference`;一律手寫 + `var(--token)`;不裸 hex;不 `outline:none` 蓋 focus ring;尊重 `prefers-reduced-motion`。
- 後端測試 DB 隔離:每測試獨立 temp SQLite(`Data Source={tmp};Foreign Keys=True`)+ `Database.Migrate()`。
- 前端測試:`npm test -- --no-watch`(= ng test/Vitest);e2e `npm run e2e`。
- 每個 task 結束 build/測試綠後再 commit。

---

# Phase A — 單張重新處理(後端 + Inspector)

## Task A1: `ImageReprocessor`(Pm.Scanner)

**Files:**
- Create: `src/Pm.Scanner/IImageReprocessor.cs`
- Create: `src/Pm.Scanner/ImageReprocessor.cs`
- Test: `tests/Pm.Scanner.Tests/ImageReprocessorTests.cs`

**Interfaces:**
- Consumes: `IImageMetadataReader.Read(string) → ImageMeta(int? Width, Height, string? Mime, DateTimeOffset? TakenAt, string? CameraModel, double? GpsLat, GpsLon, string? ExifJson)`;`IThumbnailService.PathFor(string)`、`GenerateAsync(string absPath, string hash, CancellationToken) → string?`;`Photo`(可變屬性 Width/Height/Mime/TakenAt/CameraModel/GpsLat/GpsLon/Exif、唯讀 FileHash)。
- Produces:
  ```csharp
  public sealed record ReprocessResult(bool Decoded, bool ThumbGenerated);
  public interface IImageReprocessor
  {
      // 改寫 photo 的影像衍生欄位(呼叫端負責 SaveChanges);不動 hash/tag。
      Task<ReprocessResult> ReprocessAsync(Photo photo, string absPath, CancellationToken ct = default);
  }
  ```

- [ ] **Step 1: 寫失敗測試**

```csharp
// tests/Pm.Scanner.Tests/ImageReprocessorTests.cs
using Pm.Data.Entities;
using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class ImageReprocessorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-rep-{Guid.NewGuid():N}");
    public ImageReprocessorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private string WriteRealPng(string name, int w = 4, int h = 2)
    {
        var path = Path.Combine(_dir, name);
        using var img = new Image<Rgba32>(w, h);
        img.SaveAsPng(path);
        return path;
    }

    private ImageReprocessor MakeSut(out string thumbDir)
    {
        thumbDir = Path.Combine(_dir, "thumbs");
        var thumbs = new ThumbnailService(new ThumbnailOptions { Dir = thumbDir, MaxEdge = 64 });
        return new ImageReprocessor(new ExifImageMetadataReader(), thumbs);
    }

    [Fact]
    public async Task Decodable_image_fills_metadata_and_generates_thumb()
    {
        var path = WriteRealPng("a.png", 4, 2);
        var sut = MakeSut(out var thumbDir);
        var photo = new Photo { FileHash = "ab" + new string('0', 62) };

        var result = await sut.ReprocessAsync(photo, path);

        Assert.True(result.Decoded);
        Assert.True(result.ThumbGenerated);
        Assert.Equal(4, photo.Width);
        Assert.Equal(2, photo.Height);
        Assert.Equal("image/png", photo.Mime);
        Assert.True(File.Exists(Path.Combine(thumbDir, "ab", "00", photo.FileHash + ".webp")));
    }

    [Fact]
    public async Task Undecodable_file_reports_not_decoded_and_no_thumb()
    {
        var path = Path.Combine(_dir, "bad.png");
        File.WriteAllText(path, "not an image");
        var sut = MakeSut(out var thumbDir);
        var photo = new Photo { FileHash = "cd" + new string('0', 62) };

        var result = await sut.ReprocessAsync(photo, path);

        Assert.False(result.Decoded);
        Assert.False(result.ThumbGenerated);
        Assert.Null(photo.Width);
        Assert.False(File.Exists(Path.Combine(thumbDir, "cd", "00", photo.FileHash + ".webp")));
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Scanner.Tests --filter ImageReprocessorTests`
Expected: FAIL — 找不到 `IImageReprocessor`/`ImageReprocessor`/`ReprocessResult`。

- [ ] **Step 3: 寫最小實作**

```csharp
// src/Pm.Scanner/IImageReprocessor.cs
namespace Pm.Scanner;
using Pm.Data.Entities;

public sealed record ReprocessResult(bool Decoded, bool ThumbGenerated);

public interface IImageReprocessor
{
    Task<ReprocessResult> ReprocessAsync(Photo photo, string absPath, CancellationToken ct = default);
}
```

```csharp
// src/Pm.Scanner/ImageReprocessor.cs
namespace Pm.Scanner;
using Pm.Data.Entities;

public sealed class ImageReprocessor(IImageMetadataReader meta, IThumbnailService thumbs) : IImageReprocessor
{
    public async Task<ReprocessResult> ReprocessAsync(Photo photo, string absPath, CancellationToken ct = default)
    {
        var m = meta.Read(absPath);
        photo.Width = m.Width;
        photo.Height = m.Height;
        photo.Mime = m.Mime;
        photo.TakenAt = m.TakenAt;
        photo.CameraModel = m.CameraModel;
        photo.GpsLat = m.GpsLat;
        photo.GpsLon = m.GpsLon;
        photo.Exif = m.ExifJson;

        var decoded = m.Width is not null;
        if (!decoded) return new ReprocessResult(false, false);

        // GenerateAsync 內部 File.Replace → 覆蓋既有縮圖(force 語意)。
        var thumb = await thumbs.GenerateAsync(absPath, photo.FileHash, ct) is not null;
        return new ReprocessResult(true, thumb);
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Pm.Scanner.Tests --filter ImageReprocessorTests`
Expected: PASS(2 passed)。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Scanner/IImageReprocessor.cs src/Pm.Scanner/ImageReprocessor.cs tests/Pm.Scanner.Tests/ImageReprocessorTests.cs
git commit -m "feat(scanner): ImageReprocessor — 重新解碼補 metadata + 強制重產縮圖"
```

---

## Task A2: 接線 DI + `LibraryScanner` 建構子

**Files:**
- Modify: `src/Pm.Scanner/LibraryScanner.cs:7-12`(兩個建構子加 `IImageReprocessor`)
- Modify: `src/Pm.Api/Configuration/ServiceRegistration.cs`(註冊 `IImageReprocessor`)

**Interfaces:**
- Produces:`LibraryScanner` 主建構子新增第 5 參數 `IImageReprocessor reprocessor`;欄位於後續 task 使用。

- [ ] **Step 1: 改 `LibraryScanner` 建構子**

把 `src/Pm.Scanner/LibraryScanner.cs` 開頭兩個建構子改為:

```csharp
public sealed class LibraryScanner(
    PmDbContext db, IFileHasher hasher, IImageMetadataReader meta, IThumbnailService thumbs,
    IImageReprocessor reprocessor)
{
    // 便利建構子:既有呼叫端(只給 db+hasher)沿用預設 reader/thumb/reprocessor。
    public LibraryScanner(PmDbContext db, IFileHasher hasher)
        : this(db, hasher, new ExifImageMetadataReader(), new ThumbnailService(new ThumbnailOptions()),
               new ImageReprocessor(new ExifImageMetadataReader(), new ThumbnailService(new ThumbnailOptions()))) { }
```

- [ ] **Step 2: 註冊 DI**

在 `src/Pm.Api/Configuration/ServiceRegistration.cs` 的 `AddPmServices` 內,`AddScoped<IThumbnailService, ThumbnailService>();` 之後加:

```csharp
        services.AddScoped<IImageReprocessor, ImageReprocessor>();
```

- [ ] **Step 3: build 確認綠**

Run: `dotnet build`
Expected: 建置成功，0 錯誤(既有 `LibraryScanner` 用法不變,便利建構子相容)。

- [ ] **Step 4: 跑既有掃描測試確認無回歸**

Run: `dotnet test tests/Pm.Scanner.Tests`
Expected: PASS（全綠）。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Scanner/LibraryScanner.cs src/Pm.Api/Configuration/ServiceRegistration.cs
git commit -m "chore(scanner): 注入 IImageReprocessor 進 LibraryScanner + DI 註冊"
```

---

## Task A3: 掃描快路徑自動痊癒 + `ScanResult.Healed`

**Files:**
- Modify: `src/Pm.Scanner/ScanResult.cs`(加 `Healed`)
- Modify: `src/Pm.Scanner/LibraryScanner.cs:28-29, 66-76, 106-107`
- Test: `tests/Pm.Scanner.Tests/ScanHealTests.cs`

**Interfaces:**
- Consumes:`IImageReprocessor.ReprocessAsync`、`ScanResult`。
- Produces:`ScanResult` 新增尾欄 `int Healed`。

- [ ] **Step 1: 寫失敗測試**

```csharp
// tests/Pm.Scanner.Tests/ScanHealTests.cs
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Microsoft.Data.Sqlite;
using Xunit;

public class ScanHealTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-heal-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-healroot-{Guid.NewGuid():N}");
    private readonly string _thumbDir;
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public ScanHealTests()
    {
        Directory.CreateDirectory(_root);
        _thumbDir = Path.Combine(_root, "_thumbs");
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    private LibraryScanner MakeScanner(PmDbContext ctx)
    {
        var thumbs = new ThumbnailService(new ThumbnailOptions { Dir = _thumbDir, MaxEdge = 64 });
        return new LibraryScanner(ctx, new Sha256FileHasher(), new ExifImageMetadataReader(), thumbs,
            new ImageReprocessor(new ExifImageMetadataReader(), thumbs));
    }

    [Fact]
    public async Task Rescan_heals_width_null_photo()
    {
        // Arrange:寫真實 png,先建一筆「半殘」photo(width=null、無縮圖)+ present location,mtime 對齊檔案。
        var rel = "a.png";
        var file = Path.Combine(_root, rel);
        using (var img = new Image<Rgba32>(4, 2)) img.SaveAsPng(file);
        var hash = await new Sha256FileHasher().HashFileAsync(file);
        var mtime = (DateTimeOffset)new FileInfo(file).LastWriteTimeUtc;
        long rootId, photoId;
        await using (var ctx = NewContext())
        {
            var root = new LibraryRoot { Name = "t", AbsPath = _root };
            ctx.LibraryRoots.Add(root);
            await ctx.SaveChangesAsync();
            rootId = root.Id;
            var photo = new Photo { FileHash = hash, FileSize = new FileInfo(file).Length };  // Width=null
            ctx.Photos.Add(photo);
            await ctx.SaveChangesAsync();
            photoId = photo.Id;
            ctx.PhotoLocations.Add(new PhotoLocation
            {
                PhotoId = photoId, LibraryRootId = rootId, RelPath = rel,
                Status = "present", Mtime = mtime,
                FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        // Act:重掃(快路徑會判定 size+mtime 沒變)。
        ScanResult result;
        await using (var ctx = NewContext())
            result = await MakeScanner(ctx).ScanRootAsync(rootId, enqueueTagging: true);

        // Assert:被痊癒。
        Assert.Equal(1, result.Healed);
        await using var verify = NewContext();
        var healed = await verify.Photos.FindAsync(photoId);
        Assert.Equal(4, healed!.Width);
        Assert.Equal("image/png", healed.Mime);
        Assert.True(File.Exists(Path.Combine(_thumbDir, hash[..2], hash[2..4], hash + ".webp")));
        Assert.True(await verify.TaggingJobs.AnyAsync(j => j.PhotoId == photoId));
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Scanner.Tests --filter ScanHealTests`
Expected: FAIL — `ScanResult` 無 `Healed`(編譯錯)或痊癒未發生。

- [ ] **Step 3: 加 `ScanResult.Healed`**

`src/Pm.Scanner/ScanResult.cs` 末欄後加:

```csharp
public sealed record ScanResult(
    int FilesSeen,
    int NewPhotos,
    int NewLocations,
    int SkippedUnchanged,
    int Errors,
    int ThumbsGenerated,
    int JobsQueued,
    int MarkedMissing,
    int Healed);     // width=null 半殘圖被重新處理的數量
```

- [ ] **Step 4: 改快路徑 + 回傳**

`src/Pm.Scanner/LibraryScanner.cs`:計數宣告(原 line 28-29)加 `healed`:

```csharp
        int thumbsGen = 0, jobsQueued = 0, markedMissing = 0, healed = 0;
```

快路徑分支(原 line 66-76)改為:

```csharp
                if (locInfo is not null
                    && loc is { Status: "present", Mtime: { } prevMtime }
                    && locInfo.PhotoFileSize == size
                    && (prevMtime - mtime).Duration() < TimeSpan.FromSeconds(1))
                {
                    loc.LastSeenAt = DateTimeOffset.UtcNow;
                    if (locInfo.PhotoWidth is not null)
                    {
                        thumbsGen += await GenerateThumbIfMissingAsync(file, locInfo.PhotoFileHash, ct);
                    }
                    else
                    {
                        // 半殘圖(當初解碼失敗)→ 重新處理重建 metadata/縮圖,並(視 enqueueTagging)排 WD14。
                        var photo = await db.Photos.FindAsync([loc.PhotoId], ct);
                        if (photo is not null)
                        {
                            var r = await reprocessor.ReprocessAsync(photo, file, ct);
                            if (r.ThumbGenerated) thumbsGen++;
                            if (r.Decoded)
                            {
                                healed++;
                                if (enqueueTagging)
                                {
                                    var job = await db.TaggingJobs.FindAsync([photo.Id], ct);
                                    if (job is null) db.TaggingJobs.Add(new TaggingJob { PhotoId = photo.Id });
                                    else { job.State = "pending"; job.Attempts = 0; job.UpdatedAt = DateTimeOffset.UtcNow; }
                                    jobsQueued++;
                                }
                            }
                        }
                    }
                    skipped++;
                    continue;
                }
```

回傳(原 line 106-107)加 `healed`:

```csharp
        return new ScanResult(seen, newPhotos, newLocations, skipped, errors,
            thumbsGen, jobsQueued, markedMissing, healed);
```

- [ ] **Step 5: 跑測試確認通過 + 全掃描回歸**

Run: `dotnet test tests/Pm.Scanner.Tests`
Expected: PASS（含 `ScanHealTests` 與既有掃描測試;若有測試直接 `new ScanResult(...)` 需補 `Healed` 引數）。

- [ ] **Step 6: Commit**

```bash
git add src/Pm.Scanner/ScanResult.cs src/Pm.Scanner/LibraryScanner.cs tests/Pm.Scanner.Tests/ScanHealTests.cs
git commit -m "feat(scanner): 重掃自動痊癒 width=NULL 半殘圖 + ScanResult.Healed"
```

---

## Task A4: `POST /api/photos/{id}/reprocess` 端點

**Files:**
- Modify: `src/Pm.Api/Endpoints/PhotoEndpoints.cs`(加 MapPost)
- Test: `tests/Pm.Api.Tests/PhotoReprocessTests.cs`
- 確認: `TaggingScheduler` 已於 DI 註冊(被 `TaggingEndpoints` 使用 → 應已 `AddScoped<TaggingScheduler>()`;若無則於 ServiceRegistration 補)。

**Interfaces:**
- Consumes:`IImageReprocessor`、`TaggingScheduler.ScheduleAsync(string mode, RequeueScopeDto scope)`、`RequeueScopeDto(long[]? PhotoIds=...)`。
- Produces:`POST /api/photos/{id}/reprocess` → 200 `{ decoded:bool, thumbGenerated:bool }`;404 不存在;409 無可讀 location。

- [ ] **Step 1: 寫失敗測試**

```csharp
// tests/Pm.Api.Tests/PhotoReprocessTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Pm.Data;
using Pm.Data.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class PhotoReprocessTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-repapi-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-reproot-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public PhotoReprocessTests()
    {
        Directory.CreateDirectory(_root);
        var dbPath = _dbPath;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Pm"] = $"Data Source={dbPath};Foreign Keys=True",
                ["Thumbnails:Dir"] = Path.Combine(_root, "_thumbs"),
            })));
    }
    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<long> SeedHalfDeadPhotoAsync(string rel)
    {
        var file = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using (var img = new Image<Rgba32>(6, 3)) img.SaveAsPng(file);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var root = new LibraryRoot { Name = "t", AbsPath = _root };
        db.LibraryRoots.Add(root);
        await db.SaveChangesAsync();
        var photo = new Photo { FileHash = "ee" + new string('0', 62) };  // Width=null
        db.Photos.Add(photo);
        await db.SaveChangesAsync();
        db.PhotoLocations.Add(new PhotoLocation
        {
            PhotoId = photo.Id, LibraryRootId = root.Id, RelPath = rel,
            Status = "present", FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return photo.Id;
    }

    [Fact]
    public async Task Reprocess_decodable_returns_decoded_and_fills_photo()
    {
        var id = await SeedHalfDeadPhotoAsync("a.png");
        var client = _factory.CreateClient();

        var resp = await client.PostAsync($"/api/photos/{id}/reprocess", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ReprocessBody>();
        Assert.True(body!.Decoded);
        Assert.True(body.ThumbGenerated);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var photo = await db.Photos.FindAsync(id);
        Assert.Equal(6, photo!.Width);
        Assert.Equal("image/png", photo.Mime);
    }

    [Fact]
    public async Task Reprocess_missing_photo_returns_404()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsync("/api/photos/99999/reprocess", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private sealed record ReprocessBody(bool Decoded, bool ThumbGenerated);
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Api.Tests --filter PhotoReprocessTests`
Expected: FAIL — 端點不存在(404 對 decodable 案、或路由未註冊)。

- [ ] **Step 3: 加端點**

在 `src/Pm.Api/Endpoints/PhotoEndpoints.cs` 的 `MapPhotoEndpoints` 內(緊接既有 `MapDelete("/api/photos/{id:long}", ...)` 之後)加:

```csharp
        // 單張重新處理:重新解碼 → 補 metadata + 強制重產縮圖 → refresh WD14(清舊 wd14 + 重排)。
        app.MapPost("/api/photos/{id:long}/reprocess", async (
            long id, PmDbContext db, IImageReprocessor reprocessor, TaggingScheduler scheduler) =>
        {
            var photo = await db.Photos.Include(p => p.Locations).FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null) return Results.NotFound();

            var loc = photo.Locations.FirstOrDefault(l => l.Status == "present");
            if (loc is null) return Results.Json(new { error = "no readable location" }, statusCode: 409);

            var root = await db.LibraryRoots.FindAsync(loc.LibraryRootId);
            if (root is null) return Results.Json(new { error = "root missing" }, statusCode: 409);
            var absPath = Path.GetFullPath(Path.Combine(root.AbsPath, loc.RelPath.Replace('/', Path.DirectorySeparatorChar)));

            var result = await reprocessor.ReprocessAsync(photo, absPath);
            await db.SaveChangesAsync();

            if (result.Decoded)
                await scheduler.ScheduleAsync("refresh", new RequeueScopeDto(PhotoIds: [id]));

            return Results.Ok(new { decoded = result.Decoded, thumbGenerated = result.ThumbGenerated });
        })
            .WithTags("Photos");
```

需要的 `using`(若檔案頂端未有):`using Microsoft.EntityFrameworkCore;`、`using Pm.Scanner;`。`IImageReprocessor`、`TaggingScheduler`、`RequeueScopeDto` 皆 DI 可解析(A2 已註冊 reprocessor;TaggingScheduler 既有)。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Pm.Api.Tests --filter PhotoReprocessTests`
Expected: PASS(2 passed)。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Api/Endpoints/PhotoEndpoints.cs tests/Pm.Api.Tests/PhotoReprocessTests.cs
git commit -m "feat(api): POST /api/photos/{id}/reprocess — 重新處理 + refresh WD14"
```

---

## Task A5: 前端 API 接線(`PmApi.reprocess` + store)

**Files:**
- Modify: `src/Pm.Web/src/app/core/api/pm-api.ts`(加 `reprocess`)
- Modify: `src/Pm.Web/src/app/features/inspector/inspector.store.ts`(加 `reprocess`)

**Interfaces:**
- Produces:`PmApi.reprocess(photoId: number): Promise<{ decoded: boolean; thumbGenerated: boolean }>`;`InspectorStore.reprocess(photoId: number): Promise<{ decoded: boolean; thumbGenerated: boolean }>`。

- [ ] **Step 1: 加 `PmApi.reprocess`**

在 `src/Pm.Web/src/app/core/api/pm-api.ts` 既有 `retag(...)` 方法之後加:

```typescript
  reprocess(photoId: number): Promise<{ decoded: boolean; thumbGenerated: boolean }> {
    return firstValueFrom(
      this.http.post<{ decoded: boolean; thumbGenerated: boolean }>(
        `/api/photos/${photoId}/reprocess`, null),
    );
  }
```

- [ ] **Step 2: 加 `InspectorStore.reprocess`**

在 `src/Pm.Web/src/app/features/inspector/inspector.store.ts` 既有 `retag(...)` 之後加(重新處理後刷新該張詳情):

```typescript
  async reprocess(photoId: number): Promise<{ decoded: boolean; thumbGenerated: boolean }> {
    const result = await this.api.reprocess(photoId);
    if (this.currentId === photoId) await this.refresh();
    return result;
  }
```

> 註:`currentId` / `refresh()` 沿用 store 既有成員(見 `retag` 實作)。

- [ ] **Step 3: build 前端確認綠**

Run: `cd src/Pm.Web && npm run build`
Expected: 建置成功。

- [ ] **Step 4: Commit**

```bash
git add src/Pm.Web/src/app/core/api/pm-api.ts src/Pm.Web/src/app/features/inspector/inspector.store.ts
git commit -m "feat(web): PmApi.reprocess + InspectorStore.reprocess"
```

---

## Task A6: Inspector 動作列(砍重標、加重新處理)

**Files:**
- Modify: `src/Pm.Web/src/app/features/inspector/inspector/inspector.ts`(加 `reprocess()` + `reprocessing`/`reprocessMsg` signal,移除 `retag('refresh')` 綁定保留 `retag('clear')`)
- Modify: `src/Pm.Web/src/app/features/inspector/inspector/inspector.html`(新動作列、砍重標、移走清除自動標、失敗訊息)
- Modify: `src/Pm.Web/src/app/features/inspector/inspector/inspector.css`(動作列樣式)
- Test: `src/Pm.Web/src/app/features/inspector/inspector/inspector.spec.ts`

**Interfaces:**
- Consumes:`InspectorStore.reprocess`、`InspectorStore.retag`。

- [ ] **Step 1: 寫失敗測試**

```typescript
// src/Pm.Web/src/app/features/inspector/inspector/inspector.spec.ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Inspector } from './inspector';
import { InspectorStore } from '../inspector.store';

describe('Inspector reprocess action', () => {
  function make(storeStub: Partial<InspectorStore>) {
    TestBed.configureTestingModule({
      imports: [Inspector],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: InspectorStore, useValue: storeStub },
      ],
    });
    return TestBed.createComponent(Inspector);
  }

  it('calls store.reprocess and shows no error on decoded', async () => {
    let called = 0;
    const fixture = make({
      reprocess: async () => { called++; return { decoded: true, thumbGenerated: true }; },
      photo: () => ({ id: 7 }) as any,
    } as any);
    fixture.componentInstance['photoId'] = (() => 7) as any;
    await fixture.componentInstance.reprocess();
    expect(called).toBe(1);
    expect(fixture.componentInstance.reprocessMsg()).toBe('');
  });

  it('shows failure message when not decoded', async () => {
    const fixture = make({
      reprocess: async () => ({ decoded: false, thumbGenerated: false }),
      photo: () => ({ id: 7 }) as any,
    } as any);
    fixture.componentInstance['photoId'] = (() => 7) as any;
    await fixture.componentInstance.reprocess();
    expect(fixture.componentInstance.reprocessMsg()).toContain('無法解碼');
  });
});
```

> 註:`Inspector` 取得 `photoId` 的方式以元件既有實作為準(`retag` 內用 `this.photoId()`)。測試以覆寫 `photoId` getter 模擬選取第 7 張;若元件用 `@Input`/signal input,改以對應方式設定(實作 task 時對齊既有 `photoId` 來源)。

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web && npm test -- --no-watch`
Expected: FAIL — `reprocess` / `reprocessMsg` 不存在。

- [ ] **Step 3: 元件加方法 + signal**

`inspector.ts` 在 `retagging`/`retag` 附近加:

```typescript
  readonly reprocessing = signal(false);
  readonly reprocessMsg = signal('');

  async reprocess(): Promise<void> {
    const id = this.photoId();
    if (id == null || this.reprocessing()) return;
    this.reprocessing.set(true);
    this.reprocessMsg.set('');
    try {
      const r = await this.store.reprocess(id);
      if (!r.decoded) this.reprocessMsg.set('無法解碼這張圖 —— 可能損毀或格式不支援');
    } finally {
      this.reprocessing.set(false);
    }
  }
```

- [ ] **Step 4: 改 template**

`inspector.html`:在檔名 `<div class="iname">{{ fileName(p) }}</div>`(line 14)之後插入動作列:

```html
    <!-- photo 層級動作:重新處理(主)+ 清除自動標(次/destructive)。重標已併入重新處理。 -->
    <div class="iactions">
      <button class="act" type="button" [disabled]="reprocessing()" (click)="reprocess()"
        title="重新解碼這張圖:補尺寸/MIME、重產縮圖、重排 WD14 自動標">
        {{ reprocessing() ? '處理中…' : '↻ 重新處理' }}
      </button>
      <button class="act ghost" type="button" [disabled]="retagging()" (click)="retag('clear')"
        title="清除這張圖的 WD14 自動標(保留手動／路徑標)">清除自動標</button>
    </div>
    @if (reprocessMsg()) { <div class="iactions-msg">{{ reprocessMsg() }}</div> }
```

並把標籤分區標題(line 43-48)那兩顆 `.mini` 移除(刪掉 `重標` 與 `清除自動標` 兩個 button),只留標題:

```html
      <div class="isec-h">標籤<span class="line"></span></div>
```

- [ ] **Step 5: 加樣式**

`inspector.css` 末尾加(全用 token、不裸 hex):

```css
.iactions { display: flex; gap: 8px; margin: 4px 0 10px; }
.iactions .act {
  font-size: 11px; font-weight: 500; color: var(--color-text);
  background: var(--color-raised); border: 1px solid var(--color-hair);
  border-radius: var(--radius-soft); padding: 5px 10px; cursor: pointer;
}
.iactions .act:hover:not(:disabled) { border-color: var(--color-accent); }
.iactions .act.ghost { color: var(--color-muted); background: transparent; }
.iactions .act.ghost:hover:not(:disabled) { color: var(--color-text); border-color: var(--color-muted); }
.iactions .act:disabled { opacity: 0.4; cursor: default; }
.iactions-msg { font-size: 11px; color: var(--color-warning); margin: -4px 0 10px; }
```

- [ ] **Step 6: 跑測試 + build**

Run: `cd src/Pm.Web && npm test -- --no-watch && npm run build`
Expected: PASS + 建置成功。

- [ ] **Step 7: Commit**

```bash
git add src/Pm.Web/src/app/features/inspector/inspector/
git commit -m "feat(web): Inspector 動作列 — 重新處理 + 清除自動標,砍冗餘重標"
```

---

# Phase B — 前端 RWD(桌面縮放韌性)

## Task B1: `computeMasonryLayout` 純函式

**Files:**
- Create: `src/Pm.Web/src/app/core/masonry-layout.ts`
- Test: `src/Pm.Web/src/app/core/masonry-layout.spec.ts`

**Interfaces:**
- Produces:
  ```typescript
  export interface MasonryBox { left: number; top: number; width: number; height: number; }
  export interface MasonryLayout { cols: number; colWidth: number; boxes: MasonryBox[]; containerHeight: number; }
  export function computeMasonryLayout(containerWidth: number, aspects: number[], minColWidth: number, gap: number): MasonryLayout;
  ```

- [ ] **Step 1: 寫失敗測試**

```typescript
// src/Pm.Web/src/app/core/masonry-layout.spec.ts
import { computeMasonryLayout } from './masonry-layout';

describe('computeMasonryLayout', () => {
  it('never returns 0 columns for positive width', () => {
    expect(computeMasonryLayout(100, [1], 180, 12).cols).toBe(1);
  });

  it('column count grows with width', () => {
    const gap = 12, min = 180;
    expect(computeMasonryLayout(180, [1, 1], min, gap).cols).toBe(1);
    expect(computeMasonryLayout(372, [1, 1], min, gap).cols).toBe(2); // (372+12)/(180+12)=2
    expect(computeMasonryLayout(600, [1, 1], min, gap).cols).toBe(3);
  });

  it('computes column width accounting for gaps', () => {
    const l = computeMasonryLayout(372, [1, 1], 180, 12); // 2 cols
    expect(l.colWidth).toBeCloseTo((372 - 12) / 2); // 180
  });

  it('places items into shortest column (greedy)', () => {
    // 2 cols, all aspect 1 → square boxes; 3rd item goes back to col0.
    const l = computeMasonryLayout(372, [1, 1, 1], 180, 12);
    expect(l.boxes[0].left).toBeCloseTo(0);
    expect(l.boxes[1].left).toBeCloseTo(180 + 12);
    expect(l.boxes[2].left).toBeCloseTo(0);
    expect(l.boxes[2].top).toBeCloseTo(180 + 12);
  });

  it('uses 1:1 fallback for non-positive aspect', () => {
    const l = computeMasonryLayout(372, [0], 180, 12);
    expect(l.boxes[0].height).toBeCloseTo(180); // colWidth / 1
  });

  it('returns empty layout for non-positive width', () => {
    expect(computeMasonryLayout(0, [1], 180, 12)).toEqual({ cols: 0, colWidth: 0, boxes: [], containerHeight: 0 });
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web && npm test -- --no-watch`
Expected: FAIL — 模組不存在。

- [ ] **Step 3: 實作**

```typescript
// src/Pm.Web/src/app/core/masonry-layout.ts
export interface MasonryBox { left: number; top: number; width: number; height: number; }
export interface MasonryLayout { cols: number; colWidth: number; boxes: MasonryBox[]; containerHeight: number; }

/** 依容器寬與各格長寬比算瀑布流座標。最少 1 欄,永不破版;以已知 aspect 直接算高,不量 DOM。 */
export function computeMasonryLayout(
  containerWidth: number, aspects: number[], minColWidth: number, gap: number): MasonryLayout {
  if (containerWidth <= 0) return { cols: 0, colWidth: 0, boxes: [], containerHeight: 0 };

  const cols = Math.max(1, Math.floor((containerWidth + gap) / (minColWidth + gap)));
  const colWidth = (containerWidth - gap * (cols - 1)) / cols;
  const colHeights = new Array<number>(cols).fill(0);

  const boxes: MasonryBox[] = aspects.map((aspect) => {
    const a = aspect > 0 ? aspect : 1;
    const height = colWidth / a;
    let c = 0;
    for (let i = 1; i < cols; i++) if (colHeights[i] < colHeights[c]) c = i;
    const left = c * (colWidth + gap);
    const top = colHeights[c];
    colHeights[c] = top + height + gap;
    return { left, top, width: colWidth, height };
  });

  const tallest = colHeights.reduce((m, h) => (h > m ? h : m), 0);
  const containerHeight = boxes.length ? tallest - gap : 0;
  return { cols, colWidth, boxes, containerHeight };
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd src/Pm.Web && npm test -- --no-watch`
Expected: PASS（6 passed）。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/masonry-layout.ts src/Pm.Web/src/app/core/masonry-layout.spec.ts
git commit -m "feat(web): computeMasonryLayout 純函式(aspect 驅動、最少 1 欄)"
```

---

## Task B2: 斷點常數 + 收合決策純函式

**Files:**
- Create: `src/Pm.Web/src/app/core/layout-breakpoints.ts`
- Test: `src/Pm.Web/src/app/core/layout-breakpoints.spec.ts`

**Interfaces:**
- Produces:
  ```typescript
  export const INSPECTOR_COLLAPSE = 1180;
  export const FACET_COLLAPSE = 940;
  export const MASONRY_GAP = 12;
  export const MIN_COL_WIDTH: { readonly dense: number; readonly standard: number; readonly large: number };
  export function shouldAutoCollapse(stageWidth: number, threshold: number): boolean;
  ```

- [ ] **Step 1: 寫失敗測試**

```typescript
// src/Pm.Web/src/app/core/layout-breakpoints.spec.ts
import { shouldAutoCollapse, INSPECTOR_COLLAPSE, FACET_COLLAPSE } from './layout-breakpoints';

describe('layout breakpoints', () => {
  it('collapses below threshold', () => {
    expect(shouldAutoCollapse(INSPECTOR_COLLAPSE - 1, INSPECTOR_COLLAPSE)).toBe(true);
    expect(shouldAutoCollapse(FACET_COLLAPSE - 1, FACET_COLLAPSE)).toBe(true);
  });
  it('does not collapse at or above threshold', () => {
    expect(shouldAutoCollapse(INSPECTOR_COLLAPSE, INSPECTOR_COLLAPSE)).toBe(false);
    expect(shouldAutoCollapse(2000, INSPECTOR_COLLAPSE)).toBe(false);
  });
  it('treats non-positive width as not collapsed (unmeasured)', () => {
    expect(shouldAutoCollapse(0, INSPECTOR_COLLAPSE)).toBe(false);
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web && npm test -- --no-watch`
Expected: FAIL — 模組不存在。

- [ ] **Step 3: 實作**

```typescript
// src/Pm.Web/src/app/core/layout-breakpoints.ts
// 斷點單一真相源(TS 常數;CSS @media 不能吃 var())。
export const INSPECTOR_COLLAPSE = 1180; // stage 寬 < 此 → inspector 自動收
export const FACET_COLLAPSE = 940;      // stage 寬 < 此 → facet / 資料夾樹 自動收
export const MASONRY_GAP = 12;
export const MIN_COL_WIDTH = { dense: 150, standard: 180, large: 280 } as const;

/** stage 寬量到(>0)且小於門檻 → 該自動收。未量到(<=0)視為不收,避免初始抖動。 */
export function shouldAutoCollapse(stageWidth: number, threshold: number): boolean {
  return stageWidth > 0 && stageWidth < threshold;
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd src/Pm.Web && npm test -- --no-watch`
Expected: PASS（3 passed）。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/layout-breakpoints.ts src/Pm.Web/src/app/core/layout-breakpoints.spec.ts
git commit -m "feat(web): 斷點常數 + shouldAutoCollapse 決策純函式"
```

---

## Task B3: `useStageWidth` 寬度 signal util

**Files:**
- Create: `src/Pm.Web/src/app/core/use-stage-width.ts`

**Interfaces:**
- Consumes:Angular `ElementRef`、`DestroyRef`、`signal`。
- Produces:`export function useStageWidth(host: ElementRef<HTMLElement>, destroyRef: DestroyRef): Signal<number>`(回唯讀寬度 signal,內掛 `ResizeObserver` + rAF debounce,`destroyRef` 時清理)。

- [ ] **Step 1: 實作(此 util 屬 DOM 接線,以 e2e + 手測覆蓋,不寫單元測試)**

```typescript
// src/Pm.Web/src/app/core/use-stage-width.ts
import { DestroyRef, ElementRef, Signal, signal } from '@angular/core';

/** 量測 host 元素寬度的唯讀 signal;ResizeObserver + requestAnimationFrame debounce。 */
export function useStageWidth(host: ElementRef<HTMLElement>, destroyRef: DestroyRef): Signal<number> {
  const width = signal(host.nativeElement.getBoundingClientRect().width);
  let raf = 0;
  const ro = new ResizeObserver((entries) => {
    const w = entries[0]?.contentRect.width ?? 0;
    cancelAnimationFrame(raf);
    raf = requestAnimationFrame(() => width.set(w));
  });
  ro.observe(host.nativeElement);
  destroyRef.onDestroy(() => { cancelAnimationFrame(raf); ro.disconnect(); });
  return width.asReadonly();
}
```

- [ ] **Step 2: build 確認綠**

Run: `cd src/Pm.Web && npm run build`
Expected: 建置成功。

- [ ] **Step 3: Commit**

```bash
git add src/Pm.Web/src/app/core/use-stage-width.ts
git commit -m "feat(web): useStageWidth — ResizeObserver 寬度 signal util"
```

---

## Task B4: `<app-masonry>` 共用元件

**Files:**
- Create: `src/Pm.Web/src/app/core/ui/masonry.ts`
- Test: `src/Pm.Web/src/app/core/ui/masonry.spec.ts`

**Interfaces:**
- Consumes:`computeMasonryLayout`、`useStageWidth`。
- Produces:`selector: 'app-masonry'`;inputs `items: unknown[]`、`aspect: (item: unknown) => number`、`minColWidth = 180`、`gap = 12`;內容投影 `<ng-template let-item let-i="index">`。對外暴露 `cols()` signal 供測試/e2e 斷言。

- [ ] **Step 1: 寫失敗測試**

```typescript
// src/Pm.Web/src/app/core/ui/masonry.spec.ts
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Masonry } from './masonry';

@Component({
  standalone: true,
  imports: [Masonry],
  template: `
    <div style="width:600px">
      <app-masonry [items]="items" [aspect]="aspect" [minColWidth]="180" [gap]="12">
        <ng-template let-item><div class="cell">{{ item }}</div></ng-template>
      </app-masonry>
    </div>`,
})
class Host { items = [1, 2, 3]; aspect = () => 1; }

describe('app-masonry', () => {
  it('renders one positioned wrapper per item', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const cells = fixture.nativeElement.querySelectorAll('.cell');
    expect(cells.length).toBe(3);
    const wrappers = fixture.nativeElement.querySelectorAll('.m-item');
    expect(wrappers.length).toBe(3);
    expect((wrappers[0] as HTMLElement).style.position).toBe('absolute');
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web && npm test -- --no-watch`
Expected: FAIL — 元件不存在。

- [ ] **Step 3: 實作元件**

```typescript
// src/Pm.Web/src/app/core/ui/masonry.ts
import {
  AfterContentInit, Component, ContentChild, DestroyRef, ElementRef, Input,
  TemplateRef, computed, inject, signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { computeMasonryLayout } from '../masonry-layout';
import { useStageWidth } from '../use-stage-width';

@Component({
  selector: 'app-masonry',
  standalone: true,
  imports: [NgTemplateOutlet],
  template: `
    <div class="m-root" [style.height.px]="layout().containerHeight">
      @for (item of items; track $index) {
        <div class="m-item"
          [style.left.px]="layout().boxes[$index]?.left"
          [style.top.px]="layout().boxes[$index]?.top"
          [style.width.px]="layout().boxes[$index]?.width">
          <ng-container *ngTemplateOutlet="tpl; context: { $implicit: item, index: $index }" />
        </div>
      }
    </div>`,
  styles: [`
    .m-root { position: relative; width: 100%; }
    .m-item { position: absolute; }
  `],
})
export class Masonry implements AfterContentInit {
  private readonly hostRef = inject(ElementRef<HTMLElement>);
  private readonly destroyRef = inject(DestroyRef);

  @Input() items: unknown[] = [];
  @Input() aspect: (item: unknown) => number = () => 1;
  @Input() minColWidth = 180;
  @Input() gap = 12;

  @ContentChild(TemplateRef) tpl!: TemplateRef<unknown>;

  private readonly width = signal(0);
  readonly layout = computed(() =>
    computeMasonryLayout(this.width(), this.items.map((i) => this.aspect(i)), this.minColWidth, this.gap));
  readonly cols = computed(() => this.layout().cols);

  ngAfterContentInit(): void {
    const w = useStageWidth(this.hostRef, this.destroyRef);
    // 鏡射到本地 signal,讓 layout computed 連動。
    queueMicrotask(() => this.width.set(w()));
    // ResizeObserver 已在 useStageWidth 內;此處再建一個輕量 effect 由 w() 推動。
    const sync = () => { this.width.set(w()); requestAnimationFrame(sync); };
    requestAnimationFrame(sync);
  }
}
```

> 註:測試環境無實際 layout 尺寸,容器寬可能為 0 → `computeMasonryLayout` 回空。為讓單元測試能斷言「每個 item 一個 wrapper」,模板以 `items` 迴圈渲染 wrapper(即使 box 尚未算出,`left/top/width` 綁定為 undefined 不影響 wrapper 數量)。實際定位由 ResizeObserver 量到寬後生效(e2e 覆蓋真實版面)。

- [ ] **Step 4: 跑測試確認通過 + build**

Run: `cd src/Pm.Web && npm test -- --no-watch && npm run build`
Expected: PASS + 建置成功。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/core/ui/masonry.ts src/Pm.Web/src/app/core/ui/masonry.spec.ts
git commit -m "feat(web): <app-masonry> 共用瀑布流元件(內容投影 + 絕對定位)"
```

---

## Task B5: `photo-grid` 接 `<app-masonry>`,移除 column-count

**Files:**
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.ts`(import Masonry、`aspectNum`)
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.html`(改用 `<app-masonry>` + `<ng-template>`)
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css`(刪 `.masonry` column-count 相關)

**Interfaces:**
- Consumes:`Masonry`(`app-masonry`)、`MIN_COL_WIDTH`、`MASONRY_GAP`。

- [ ] **Step 1: 元件 import + aspect 數值版**

`photo-grid.ts`:`imports` 陣列加 `Masonry`;加數值 aspect 與欄寬選擇(沿用既有檢視模式 signal,假設為 `mode()` ∈ 'dense'|'standard'|'large';若名稱不同,對齊既有):

```typescript
import { Masonry } from '../../../core/ui/masonry';
import { MIN_COL_WIDTH, MASONRY_GAP } from '../../../core/layout-breakpoints';
// ...
  readonly gap = MASONRY_GAP;
  aspectNum = (p: PhotoListItem): number => (p.width && p.height ? p.width / p.height : 1);
  minColWidth(): number {
    const m = this.mode();
    return m === 'dense' ? MIN_COL_WIDTH.dense : m === 'large' ? MIN_COL_WIDTH.large : MIN_COL_WIDTH.standard;
  }
```

- [ ] **Step 2: 改 template**

`photo-grid.html`:把原 `.masonry` 容器(含 `@for` tile 迴圈)換成:

```html
<app-masonry [items]="store.photos()" [aspect]="aspectNum" [minColWidth]="minColWidth()" [gap]="gap">
  <ng-template let-p>
    <div class="tile" [class.sel]="selectedId() === p.id" (click)="pick(p)">
      <app-thumb [photoId]="p.id" [aspectRatio]="aspect(p)" />
    </div>
  </ng-template>
</app-masonry>
```

> 保留既有 `aspect(p)`(回字串給 `app-thumb`)與 `aspectNum`(回數值給 masonry)。tile 的 hover/選取樣式不變。

- [ ] **Step 3: 刪 column-count CSS**

`photo-grid.css`:刪除 `.masonry { column-count … }` 及其 `@media` 與 `.masonry.dense/.large` 的 column-count 規則(B4 的 `.m-root` 已接管排版)。tile 的 `:hover { transform: translateY(-3px) }` 等互動樣式**保留**(定位用 left/top,不佔 transform)。

- [ ] **Step 4: build + 起 app 手測 gallery 圖牆正常**

Run: `cd src/Pm.Web && npm run build`
Expected: 建置成功;（手測留待 Task B10 e2e + 最終驗證一併做）。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/photo-grid/
git commit -m "feat(web): photo-grid 改用 <app-masonry>,移除寫死 column-count"
```

---

## Task B6: `browse-grid` 接 `<app-masonry>`

**Files:**
- Modify: `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.ts`
- Modify: `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.html`(或 inline template)
- Modify: `src/Pm.Web/src/app/features/browse/browse-grid/browse-grid.css`

- [ ] **Step 1: 同 B5 接 masonry**

`browse-grid.ts`:`imports` 加 `Masonry`;加:

```typescript
import { Masonry } from '../../../core/ui/masonry';
import { MIN_COL_WIDTH, MASONRY_GAP } from '../../../core/layout-breakpoints';
// ...
  readonly gap = MASONRY_GAP;
  readonly stdCol = MIN_COL_WIDTH.standard;
  aspectNum = (p: PhotoListItem): number => (p.width && p.height ? p.width / p.height : 1);
```

template 的 `.masonry` tile 迴圈換成:

```html
<app-masonry [items]="items()" [aspect]="aspectNum" [minColWidth]="stdCol" [gap]="gap">
  <ng-template let-p>
    <div class="tile" (click)="pick(p)">
      <app-thumb [photoId]="p.id" [aspectRatio]="aspect(p)" />
    </div>
  </ng-template>
</app-masonry>
```

> `items()`/`pick`/`aspect` 以 browse-grid 既有成員為準(對齊現有命名)。

- [ ] **Step 2: 刪 column-count CSS**

`browse-grid.css`:刪 `.masonry { column-count … }` 與其 `@media`。

- [ ] **Step 3: build 確認綠**

Run: `cd src/Pm.Web && npm run build`
Expected: 建置成功。

- [ ] **Step 4: Commit**

```bash
git add src/Pm.Web/src/app/features/browse/browse-grid/
git commit -m "feat(web): browse-grid 改用 <app-masonry>"
```

---

## Task B7: `gallery-view` 動態欄寬 + 可收合側欄

**Files:**
- Modify: `src/Pm.Web/src/app/features/gallery/gallery-view/gallery-view.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/facet-sidebar/facet-sidebar.ts` + `.css`
- Modify: `src/Pm.Web/src/app/features/inspector/inspector/inspector.css`

**Interfaces:**
- Consumes:`useStageWidth`、`shouldAutoCollapse`、`INSPECTOR_COLLAPSE`、`FACET_COLLAPSE`。

- [ ] **Step 1: gallery-view 改 JS 量測驅動的 grid 欄寬**

`gallery-view.ts`:注入 `ElementRef`/`DestroyRef`,用 `useStageWidth` 拿 stage 寬,依門檻算收合,grid 欄寬綁 inline style。把原 `grid-template-columns: 252px 1fr 350px` + `@media` 改為:

```typescript
import { Component, DestroyRef, ElementRef, computed, effect, inject, signal } from '@angular/core';
import { useStageWidth } from '../../../core/use-stage-width';
import { shouldAutoCollapse, INSPECTOR_COLLAPSE, FACET_COLLAPSE } from '../../../core/layout-breakpoints';
// ...
export class GalleryView {
  private readonly hostRef = inject(ElementRef<HTMLElement>);
  private readonly stageWidth = useStageWidth(this.hostRef, inject(DestroyRef));

  readonly facetUserCollapsed = signal<boolean | null>(null);   // null = 未手動干預
  readonly inspectorUserCollapsed = signal<boolean | null>(null);

  readonly facetCollapsed = computed(() =>
    this.facetUserCollapsed() ?? shouldAutoCollapse(this.stageWidth(), FACET_COLLAPSE));
  readonly inspectorCollapsed = computed(() =>
    this.inspectorUserCollapsed() ?? shouldAutoCollapse(this.stageWidth(), INSPECTOR_COLLAPSE));

  readonly gridCols = computed(() => {
    const f = this.facetCollapsed() ? '0' : '252px';
    const i = this.inspectorCollapsed() ? '0' : '350px';
    return `${f} 1fr ${i}`;
  });

  toggleFacet(): void { this.facetUserCollapsed.set(!this.facetCollapsed()); }
  toggleInspector(): void { this.inspectorUserCollapsed.set(!this.inspectorCollapsed()); }
}
```

template 根容器:`[style.grid-template-columns]="gridCols()"`,並把 `app-inspector` 的 `display:none` 媒體查詢移除(改由欄寬 0 + 元件自身收合處理);facet/inspector 收合狀態以 input 傳入(`[collapsed]="facetCollapsed()"` 等)或以 class 綁定。

- [ ] **Step 2: facet-sidebar / inspector 加收合**

`facet-sidebar.ts`:加 `@Input() collapsed = false;` 與一顆 toggle 鈕(emit 或呼叫父層 toggle)。`.css` 的固定 `width: 252px` 改為依 collapsed:

```css
.sidebar { width: 252px; height: 100vh; transition: width 0.15s ease; overflow: hidden; }
.sidebar.collapsed { width: 0; border-right: none; }
@media (prefers-reduced-motion: reduce) { .sidebar { transition: none; } }
```

template 根節點 `[class.collapsed]="collapsed"`。inspector 同理(`inspector.css` `:host` 寬度依 collapsed class;由 gallery-view 控制顯示)。收合後留細邊/箭頭由父層 grid 的 1fr 區塊邊緣按鈕提供(toggle 鈕放 topbar 或 stage 邊)。

- [ ] **Step 3: build 確認綠**

Run: `cd src/Pm.Web && npm run build`
Expected: 建置成功。

- [ ] **Step 4: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/gallery-view/ src/Pm.Web/src/app/features/gallery/facet-sidebar/ src/Pm.Web/src/app/features/inspector/inspector/inspector.css
git commit -m "feat(web): gallery-view JS 量測驅動欄寬 + facet/inspector 可收合"
```

---

## Task B8: `browse-view` 動態欄寬 + 資料夾樹側欄收合

**Files:**
- Modify: `src/Pm.Web/src/app/features/browse/browse-view/browse-view.ts`
- Modify: `src/Pm.Web/src/app/features/browse/folder-tree-sidebar/folder-tree-sidebar.ts` + `.css`

- [ ] **Step 1: browse-view 同 B7(只有 facet 軸 = 資料夾樹,無 inspector)**

`browse-view.ts`:

```typescript
import { Component, DestroyRef, ElementRef, computed, inject, signal } from '@angular/core';
import { useStageWidth } from '../../../core/use-stage-width';
import { shouldAutoCollapse, FACET_COLLAPSE } from '../../../core/layout-breakpoints';
// ...
export class BrowseView {
  private readonly hostRef = inject(ElementRef<HTMLElement>);
  private readonly stageWidth = useStageWidth(this.hostRef, inject(DestroyRef));
  readonly treeUserCollapsed = signal<boolean | null>(null);
  readonly treeCollapsed = computed(() =>
    this.treeUserCollapsed() ?? shouldAutoCollapse(this.stageWidth(), FACET_COLLAPSE));
  readonly gridCols = computed(() => `${this.treeCollapsed() ? '0' : '252px'} 1fr`);
  toggleTree(): void { this.treeUserCollapsed.set(!this.treeCollapsed()); }
}
```

template 根容器 `[style.grid-template-columns]="gridCols()"`;`<app-folder-tree-sidebar [collapsed]="treeCollapsed()" />`。

- [ ] **Step 2: folder-tree-sidebar 加收合**

`folder-tree-sidebar.ts`:`@Input() collapsed = false;`。`.css`:

```css
.sidebar { width: 252px; height: 100vh; transition: width 0.15s ease; overflow: hidden; }
.sidebar.collapsed { width: 0; border-right: none; }
@media (prefers-reduced-motion: reduce) { .sidebar { transition: none; } }
```

template 根 `[class.collapsed]="collapsed"`。

- [ ] **Step 3: build 確認綠**

Run: `cd src/Pm.Web && npm run build`
Expected: 建置成功。

- [ ] **Step 4: Commit**

```bash
git add src/Pm.Web/src/app/features/browse/browse-view/ src/Pm.Web/src/app/features/browse/folder-tree-sidebar/
git commit -m "feat(web): browse-view 動態欄寬 + 資料夾樹側欄可收合"
```

---

## Task B9: 雜項破版點修正

**Files:**
- Modify: `src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css`(`.ac-pop`)
- Modify: `src/Pm.Web/src/app/features/browse/inner-tag-filter/inner-tag-filter.css`(`.ac-pop`)

- [ ] **Step 1: `.ac-pop` min-width 防溢出**

`photo-grid.css`:`.ac-pop { min-width: 240px }` → `min-width: min(240px, 100%)`。
`inner-tag-filter.css`:`.ac-pop { min-width: 210px }` → `min-width: min(210px, 100%)`。

- [ ] **Step 2: build 確認綠**

Run: `cd src/Pm.Web && npm run build`
Expected: 建置成功。

- [ ] **Step 3: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/photo-grid/photo-grid.css src/Pm.Web/src/app/features/browse/inner-tag-filter/inner-tag-filter.css
git commit -m "fix(web): .ac-pop min-width 改 min(…,100%) 防窄寬溢出"
```

---

## Task B10: `rwd-resize-smoke.mjs` e2e

**Files:**
- Create: `src/Pm.Web/e2e/rwd-resize-smoke.mjs`
- Modify: `src/Pm.Web/package.json`(加 `e2e:rwd` script)

**Interfaces:**
- Consumes:既有 `e2e/browse-smoke.mjs` 的 app 啟動 + `page.route('**/api/**', …)` mock 範式。

- [ ] **Step 1: 寫 e2e(仿 browse-smoke.mjs 結構)**

```javascript
// src/Pm.Web/e2e/rwd-resize-smoke.mjs
// 縮 viewport 斷言:無橫向破版 + 圖牆 tile 可見 + 欄數隨寬遞減。
import { chromium } from 'playwright';

const BASE = process.env.PM_E2E_BASE ?? 'http://localhost:5180';
const fail = (m) => { console.error('FAIL:', m); process.exitCode = 1; throw new Error(m); };

// 與 browse-smoke.mjs 相同的 /api mock(回固定一批 photo)。
async function mockApi(page) {
  await page.route('**/api/**', (route) => {
    const url = route.request().url();
    if (url.includes('/api/search')) {
      const photos = Array.from({ length: 60 }, (_, i) => ({ id: i + 1, width: 800, height: 1000 + (i % 5) * 100 }));
      return route.fulfill({ json: { items: photos, total: 60 } });
    }
    if (url.match(/\/api\/photos\/\d+\/thumb/)) return route.fulfill({ status: 204 });
    return route.fulfill({ json: {} });
  });
}

const WIDTHS = [1400, 1100, 820, 720];

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await mockApi(page);
  await page.goto(`${BASE}/gallery`, { waitUntil: 'networkidle' });

  let prevCols = Infinity;
  for (const w of WIDTHS) {
    await page.setViewportSize({ width: w, height: 900 });
    await page.waitForTimeout(300); // rAF debounce 後重排

    const overflow = await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (overflow > 1) fail(`@${w}px 橫向破版 scrollWidth 超出 ${overflow}px`);

    const tiles = await page.$$eval('.m-item', (els) => els.length);
    if (tiles === 0) fail(`@${w}px 圖牆無可見 tile`);

    // 欄數 = distinct left 值數量
    const cols = await page.$$eval('.m-item', (els) =>
      new Set(els.map((e) => Math.round(parseFloat(getComputedStyle(e).left)))).size);
    if (cols > prevCols) fail(`@${w}px 欄數未隨寬遞減(${prevCols} → ${cols})`);
    prevCols = cols;
    console.log(`ok @${w}px: tiles=${tiles} cols=${cols} overflow=${overflow}`);
  }

  await browser.close();
  console.log('PASS rwd-resize-smoke');
})().catch((e) => { console.error(e); process.exitCode = 1; });
```

> 註:選擇器 `.m-item`(B4 的 masonry wrapper)、route 形狀(`/api/search` 回 `{items,total}`)以實際 `browse-smoke.mjs` mock 與 store 期望為準,實作時對齊。

- [ ] **Step 2: 加 npm script**

`src/Pm.Web/package.json` `scripts` 加:

```json
    "e2e:rwd": "node e2e/rwd-resize-smoke.mjs"
```

- [ ] **Step 3: 跑 e2e(需先起 app)**

Run（背景起 `dotnet run --project src/Pm.Api` 後）: `cd src/Pm.Web && npm run e2e:rwd`
Expected: `PASS rwd-resize-smoke`(各寬無破版、有 tile、欄數遞減)。

- [ ] **Step 4: Commit**

```bash
git add src/Pm.Web/e2e/rwd-resize-smoke.mjs src/Pm.Web/package.json
git commit -m "test(e2e): rwd-resize-smoke — 縮 viewport 斷言無破版 + 欄數遞減"
```

---

# 最終驗證(全部 task 完成後)

- [ ] 後端全測試:`dotnet test` → 全綠。
- [ ] 前端單元:`cd src/Pm.Web && npm test -- --no-watch` → 全綠。
- [ ] 前端 build:`npm run build` → 成功。
- [ ] e2e:`npm run e2e`(browse 回歸)+ `npm run e2e:rwd` → 皆 PASS。
- [ ] 起 app 瀏覽器手測:gallery 縮放不破版、圖牆連續自適應、側欄自動/手動收合;Inspector 對半殘圖按「重新處理」→ 縮圖/尺寸/tag 補回;真實 AVIF(photo 8c64…)流程正常。
- [ ] 截圖傳使用者手機(gallery 寬/窄兩態 + Inspector 動作列)。

# 自審紀錄

- **spec coverage**:reprocess spec §4.1→A1、§4.2→A3、§4.3→A4、§4.4→A5/A6;RWD spec §3→B1/B4/B5/B6、§2→B2、§4→B3/B7/B8、§5→B9、§6→B10。皆有對應 task。
- **placeholder**:無 TBD/TODO;每步附實際程式碼或指令。
- **type 一致**:`ReprocessResult(Decoded, ThumbGenerated)`、`computeMasonryLayout`/`MasonryLayout`、`shouldAutoCollapse`、`useStageWidth` 在跨 task 引用一致。
- **已知對齊點**(實作時依既有命名核對,非 placeholder):photo-grid 的檢視模式 signal 名(`mode()`)、browse-grid 的 `items()`/`pick`、Inspector 的 `photoId` 來源、e2e mock 的 `/api/search` 形狀 —— 這些既有成員以現場程式為準。
