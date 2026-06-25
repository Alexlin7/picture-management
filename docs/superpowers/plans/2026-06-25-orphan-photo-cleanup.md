# 孤兒 photo 清理 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 後端維護端點,預覽 + 硬刪「零 location」的孤兒 photo(async scan 舊 bug 殘留),cascade 帶走 location/tag/job,縮圖檔另刪。

**Architecture:** 在 `Program.cs` 加兩個 `.WithTags("Maintenance")` Minimal API 端點(GET 預覽 / DELETE purge)+ 啟動時 log 孤兒數。孤兒判定 `db.Photos.Where(p => !p.Locations.Any())`;purge 走既有 EF cascade,縮圖經 `IThumbnailService.PathFor(hash)` 逐筆刪。

**Tech Stack:** .NET 10 / ASP.NET Core Minimal API、EF Core + SQLite、xUnit + `WebApplicationFactory<Program>`。

## Global Constraints

- TargetFramework `net10.0`;Nullable + ImplicitUsings enable。
- 後端 TDD;測試 DB 隔離(temp SQLite 檔,沿用既有 API 測試以 `WebApplicationFactory` + `ConfigureAppConfiguration` 覆寫 `ConnectionStrings:Pm`)。
- API 只 bind localhost、無認證。
- 鐵則 #4:刪除預設軟刪,**硬刪 purge 僅明示端點**;孤兒清理以「GET 預覽 → 明示 DELETE」為閘門。
- 孤兒定義固定:`db.Photos.Where(p => !p.Locations.Any())`(零筆 location;**非**「全 archived」的失聯 photo)。
- 縮圖刪除:先收集 hash → 刪 DB → 再 `if (File.Exists(thumbs.PathFor(hash))) File.Delete(...)`;不存在不計、不拋。
- 端點回傳格式固定:GET → `{ count, ids }`;DELETE → `{ purged, thumbsDeleted }`。

---

### Task 1: GET 預覽端點

**Files:**
- Modify: `src/Pm.Api/Program.cs`(在既有 `DELETE /api/photos/{id}` 端點附近、`.WithTags("Photos")` 區塊後加新端點)
- Test: `tests/Pm.Api.Tests/OrphanCleanupApiTests.cs`(新增)

**Interfaces:**
- Produces:`GET /api/maintenance/orphan-photos` → `200 { count: int, ids: long[] }`

- [ ] **Step 1: 寫失敗測試**

`tests/Pm.Api.Tests/OrphanCleanupApiTests.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Api.Tests;

public class OrphanCleanupApiTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pm-orphan-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public OrphanCleanupApiTests()
    {
        var db = _db;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={db};Foreign Keys=True"
                })));
    }

    // 寫一筆無 location 的孤兒 photo,回其 id。
    private async Task<long> SeedOrphanAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var p = new Photo { FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ImportedAt = DateTimeOffset.UtcNow };
        ctx.Photos.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    // 寫一筆有 present location 的正常 photo,回其 id。
    private async Task<long> SeedWithLocationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var root = new LibraryRoot { Name = "r", AbsPath = @"C:\x" };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        var p = new Photo { FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ImportedAt = DateTimeOffset.UtcNow };
        ctx.Photos.Add(p);
        await ctx.SaveChangesAsync();
        ctx.PhotoLocations.Add(new PhotoLocation { PhotoId = p.Id, LibraryRootId = root.Id, RelPath = "a.png", Status = "present", FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task Preview_lists_only_orphans()
    {
        var orphanId = await SeedOrphanAsync();
        await SeedWithLocationAsync();   // 不該出現
        var client = _factory.CreateClient();

        var res = await client.GetFromJsonAsync<OrphanPreview>("/api/maintenance/orphan-photos");

        Assert.NotNull(res);
        Assert.Equal(1, res!.Count);
        Assert.Equal(new[] { orphanId }, res.Ids);
    }

    private sealed record OrphanPreview(int Count, long[] Ids);

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
```

> 註:`LibraryRoot` / `PhotoLocation` 欄位以既有 entity 為準;若 seed 欄位與 entity 不符,以 entity 定義修正(不改測試意圖)。

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter "FullyQualifiedName~OrphanCleanupApiTests"`
Expected: FAIL —— 端點不存在,GET 回 404(`GetFromJsonAsync` 擲例外或回 null)。

- [ ] **Step 3: 加 GET 端點**

`src/Pm.Api/Program.cs`,在 `DELETE /api/photos/{id:long}` 端點(`.WithTags("Photos")`)之後加:

```csharp
// 維護:孤兒 photo(零 location,async scan 舊 bug 殘留)預覽 —— 先看再刪。
app.MapGet("/api/maintenance/orphan-photos", async (PmDbContext db) =>
{
    var ids = await db.Photos.Where(p => !p.Locations.Any()).Select(p => p.Id).ToListAsync();
    return Results.Ok(new { count = ids.Count, ids });
})
    .WithTags("Maintenance");
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter "FullyQualifiedName~OrphanCleanupApiTests"`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Api/Program.cs tests/Pm.Api.Tests/OrphanCleanupApiTests.cs
git commit -m "feat(api): 孤兒 photo 預覽端點 GET /api/maintenance/orphan-photos"
```

---

### Task 2: DELETE purge 端點(cascade + 縮圖)

**Files:**
- Modify: `src/Pm.Api/Program.cs`(GET 端點之後)
- Test: `tests/Pm.Api.Tests/OrphanCleanupApiTests.cs`(加測試)

**Interfaces:**
- Consumes:`IThumbnailService.PathFor(string hash) -> string`(既有;`Program.cs` 既有 thumb 端點已用)
- Produces:`DELETE /api/maintenance/orphan-photos` → `200 { purged: int, thumbsDeleted: int }`

- [ ] **Step 1: 寫失敗測試(cascade + 縮圖刪 + 不誤刪 + 冪等)**

在 `OrphanCleanupApiTests` 加(沿用 Task 1 的 seed helper):

```csharp
[Fact]
public async Task Purge_deletes_orphans_cascade_and_thumb_but_keeps_located()
{
    // 孤兒帶 photo_tag + tagging_job + 縮圖檔
    long orphanId; string orphanHash;
    using (var scope = _factory.Services.CreateScope())
    {
        var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var p = new Photo { FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ImportedAt = DateTimeOffset.UtcNow };
        ctx.Photos.Add(p);
        await ctx.SaveChangesAsync();
        orphanId = p.Id; orphanHash = p.FileHash;
        var tag = new Tag { Name = "x", Kind = "manual" };
        ctx.Tags.Add(tag);
        await ctx.SaveChangesAsync();
        ctx.PhotoTags.Add(new PhotoTag { PhotoId = p.Id, TagId = tag.Id, Source = "manual" });
        ctx.TaggingJobs.Add(new TaggingJob { PhotoId = p.Id, State = "pending", UpdatedAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();

        // 造一個縮圖檔
        var thumbs = scope.ServiceProvider.GetRequiredService<Pm.Scanner.IThumbnailService>();
        var tp = thumbs.PathFor(orphanHash);
        Directory.CreateDirectory(Path.GetDirectoryName(tp)!);
        await File.WriteAllTextAsync(tp, "fake");
    }
    var locatedId = await SeedWithLocationAsync();   // 不該被刪
    var client = _factory.CreateClient();

    var res = await client.DeleteFromJsonAsync<OrphanPurge>("/api/maintenance/orphan-photos");

    Assert.NotNull(res);
    Assert.Equal(1, res!.Purged);
    Assert.Equal(1, res.ThumbsDeleted);
    using (var scope = _factory.Services.CreateScope())
    {
        var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        Assert.False(ctx.Photos.Any(p => p.Id == orphanId));               // 孤兒消失
        Assert.True(ctx.Photos.Any(p => p.Id == locatedId));               // 正常的留著
        Assert.False(ctx.PhotoTags.Any(pt => pt.PhotoId == orphanId));     // cascade
        Assert.False(ctx.TaggingJobs.Any(j => j.PhotoId == orphanId));     // cascade
        var thumbs = scope.ServiceProvider.GetRequiredService<Pm.Scanner.IThumbnailService>();
        Assert.False(File.Exists(thumbs.PathFor(orphanHash)));             // 縮圖刪除
    }
}

[Fact]
public async Task Purge_with_no_orphans_is_idempotent()
{
    await SeedWithLocationAsync();
    var client = _factory.CreateClient();

    var res = await client.DeleteFromJsonAsync<OrphanPurge>("/api/maintenance/orphan-photos");

    Assert.NotNull(res);
    Assert.Equal(0, res!.Purged);
    Assert.Equal(0, res.ThumbsDeleted);
}

private sealed record OrphanPurge(int Purged, int ThumbsDeleted);
```

> 註:`TaggingJob` 欄位(State/UpdatedAt/PhotoId PK)、`IThumbnailService` 命名空間(`Pm.Scanner`)以實際 entity/介面為準;若 `DeleteFromJsonAsync` 在此 .NET 版本不可用,改 `var resp = await client.DeleteAsync(...); var res = await resp.Content.ReadFromJsonAsync<OrphanPurge>();`。

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter "FullyQualifiedName~OrphanCleanupApiTests"`
Expected: 新兩測 FAIL（DELETE 端點不存在 → 404 / 405)。

- [ ] **Step 3: 加 DELETE 端點**

`src/Pm.Api/Program.cs`,在 Task 1 的 GET 端點之後加:

```csharp
// 維護:硬刪孤兒 photo —— cascade 帶走 location/photo_tag/tagging_job;縮圖另刪(先取 hash 再刪 DB)。
app.MapDelete("/api/maintenance/orphan-photos", async (PmDbContext db, IThumbnailService thumbs) =>
{
    var orphans = await db.Photos.Where(p => !p.Locations.Any()).ToListAsync();
    var hashes = orphans.Select(p => p.FileHash).ToList();
    db.Photos.RemoveRange(orphans);
    await db.SaveChangesAsync();

    var thumbsDeleted = 0;
    foreach (var hash in hashes)
    {
        var path = thumbs.PathFor(hash);
        if (File.Exists(path)) { File.Delete(path); thumbsDeleted++; }
    }
    return Results.Ok(new { purged = orphans.Count, thumbsDeleted });
})
    .WithTags("Maintenance");
```

> 確認 `Program.cs` 頂部已 `using Pm.Scanner;`(`IThumbnailService` 所在)——既有 thumb 端點已用 `IThumbnailService`,故應已 in scope。

- [ ] **Step 4: 跑測試確認通過 + 全測試**

Run: `dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter "FullyQualifiedName~OrphanCleanupApiTests"` 然後 `dotnet test`
Expected: OrphanCleanupApiTests 全 PASS;全套無回歸。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Api/Program.cs tests/Pm.Api.Tests/OrphanCleanupApiTests.cs
git commit -m "feat(api): 孤兒 photo 硬刪端點 DELETE(cascade + 縮圖)"
```

---

### Task 3: 啟動時 log 孤兒數(只 log 不刪)

**Files:**
- Modify: `src/Pm.Api/Program.cs`(啟動 Migrate 區塊之後)

**Interfaces:**
- Consumes:`app.Logger`(`ILogger`)。

- [ ] **Step 1: 加啟動偵測 log**

`src/Pm.Api/Program.cs`,在既有「啟動時 `db.Database.Migrate()` + WAL」的 `using (var scope ...)` 區塊**之後**加一段(可併入同一 scope 或新開一個):

```csharp
// 啟動偵測:孤兒 photo(零 location)只 log 數量、永不自動刪(清理走 /api/maintenance/orphan-photos)。
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var orphanCount = db.Photos.Count(p => !p.Locations.Any());
        if (orphanCount > 0)
            app.Logger.LogInformation(
                "啟動偵測:孤兒 photo {Count} 筆(零 location;可經 DELETE /api/maintenance/orphan-photos 清理)", orphanCount);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "孤兒 photo 啟動偵測失敗(不影響啟動)");
    }
}
```

- [ ] **Step 2: build + 全測試確認綠燈**

Run: `dotnet build` 然後 `dotnet test`
Expected: build 0 error;全測試 PASS(此段不改行為,既有測試不受影響)。

- [ ] **Step 3: 手動驗證(可選)**

Run: `dotnet run --project src/Pm.Api`,若 DB 有孤兒則啟動 log 出現該行;Ctrl+C。
Expected: 有孤兒時 console/`logs/` 出現「啟動偵測:孤兒 photo N 筆」;無孤兒則無此行。

- [ ] **Step 4: Commit**

```bash
git add src/Pm.Api/Program.cs
git commit -m "feat(api): 啟動時 log 孤兒 photo 數(只 log 不刪)"
```

---

## Self-Review

**Spec coverage(對照 `2026-06-25-orphan-photo-cleanup-design.md`):**
- §2.1(a) GET 預覽 `{count, ids}` → Task 1。✓
- §2.1(b) DELETE purge + cascade + 縮圖另刪 `{purged, thumbsDeleted}` → Task 2。✓
- §2.2 啟動 log 孤兒數(只 log) → Task 3。✓
- §2.3 鐵則 #4 明示閘門(GET 預覽 → DELETE) → Task 1 + 2 端點分離。✓
- §三 測試(預覽只列孤兒、cascade+縮圖、不誤刪、冪等) → Task 1 Step 1 + Task 2 Step 1。✓

**Placeholder scan:** 無 TBD;每 code step 有完整程式碼 + 預期輸出。entity/介面欄位以「實際定義為準」的註記是務實防呆,非待填。✓

**Type consistency:** `{count, ids}` / `{purged, thumbsDeleted}` 回傳形狀、`IThumbnailService.PathFor`、孤兒判定 `!p.Locations.Any()` 各 Task 一致。✓
