# Phase 1 路徑 → tag 確認(學習型)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **前置:** 需先完成「地基」「Scanner 身分與位置」「Scanner EXIF+縮圖+對帳」。本計畫讀 `photo_location.rel_path`、寫 `tag`/`photo_tag`/`path_tag_rule`。

**Goal:** 把資料夾結構轉成 tag,但**匯入後給使用者確認**,且**每段只問一次**:收集所有出現過的路徑段 → 已有規則的段自動套用、沒見過的列入待確認(附出現次數/範例/建議動作)→ 使用者確認後寫回 `path_tag_rule` 並套用到對應照片。內建預設:`我不知道`→ignore、四位數年份→meta_year。

**Architecture:** `Pm.Scanner` 加 `PathTagService`(收集待確認段、套用規則)與純函式 `PathTagDefaults.Suggest`。`Pm.Api` 加三個端點:列待確認段、確認規則、套用既有規則。path 來源的 tag 一律 `photo_tag.source='path'`;map_to_tag 產 `tag.kind='path'`、meta_year 產 `tag.kind='meta'`。

**Tech Stack:** .NET 10、EF Core 10.x SQLite、xUnit。

## Global Constraints

- **路徑→tag 是「匯入後確認」**,確認存 `path_tag_rule`(每段只確認一次,之後只問新段)。不得全自動硬塞。
- **tag 來源要分**:path 來源寫 `source='path'`,別跟 manual/wd14 混。
- **欄位 snake_case;SQLite 單程序**。

---

## File Structure

```
src/
├─ Pm.Scanner/
│  ├─ PathTagDefaults.cs            # 純函式:段 → 建議動作
│  ├─ PendingSegment.cs            # record
│  └─ PathTagService.cs            # 待確認段 / 套用規則
└─ Pm.Api/
   └─ Program.cs                   # +3 端點
tests/
├─ Pm.Scanner.Tests/
│  ├─ PathTagDefaultsTests.cs
│  └─ PathTagServiceTests.cs
└─ Pm.Api.Tests/
   └─ PathTagApiTests.cs
```

---

## Task 1: 待確認段收集 + 預設建議

收集某 root 下所有路徑「目錄段」,扣掉已有規則的,附出現次數/範例/建議動作。

**Files:**
- Create: `src/Pm.Scanner/PathTagDefaults.cs`
- Create: `src/Pm.Scanner/PendingSegment.cs`
- Create: `src/Pm.Scanner/PathTagService.cs`(本 task 先只含收集)
- Create: `tests/Pm.Scanner.Tests/PathTagDefaultsTests.cs`
- Create: `tests/Pm.Scanner.Tests/PathTagServiceTests.cs`

**Interfaces:**
- Consumes: `PmDbContext`、`PhotoLocation`、`PathTagRule`。
- Produces:
  - `record PendingSegment(string Segment, int Count, string SamplePath, string SuggestedAction)`
  - `PathTagDefaults.Suggest(string segment) -> string`(`"ignore"|"meta_year"|"map_to_tag"`)
  - `PathTagService(PmDbContext db)`,方法 `Task<IReadOnlyList<PendingSegment>> GetPendingSegmentsAsync(long rootId, CancellationToken ct = default)`

- [ ] **Step 1: 寫預設建議(純函式)**

Create `src/Pm.Scanner/PathTagDefaults.cs`:

```csharp
namespace Pm.Scanner;

public static class PathTagDefaults
{
    public static string Suggest(string segment)
    {
        if (segment == "我不知道") return "ignore";
        if (segment.Length == 4 && segment.All(char.IsDigit)) return "meta_year";  // 2023/2024…
        return "map_to_tag";
    }
}
```

- [ ] **Step 2: 寫 PendingSegment 與 service(收集)**

Create `src/Pm.Scanner/PendingSegment.cs`:

```csharp
namespace Pm.Scanner;

public sealed record PendingSegment(string Segment, int Count, string SamplePath, string SuggestedAction);
```

Create `src/Pm.Scanner/PathTagService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Scanner;

public sealed class PathTagService(PmDbContext db)
{
    /// <summary>此 root 下所有「目錄段」(排除檔名),扣掉已有規則者,附次數/範例/建議。</summary>
    public async Task<IReadOnlyList<PendingSegment>> GetPendingSegmentsAsync(
        long rootId, CancellationToken ct = default)
    {
        var relPaths = await db.PhotoLocations
            .Where(l => l.LibraryRootId == rootId)
            .Select(l => l.RelPath)
            .ToListAsync(ct);

        var ruled = (await db.PathTagRules
            .Where(r => r.LibraryRootId == rootId || r.LibraryRootId == null)
            .Select(r => r.Segment)
            .ToListAsync(ct)).ToHashSet();

        var count = new Dictionary<string, int>();
        var sample = new Dictionary<string, string>();
        foreach (var rel in relPaths)
        {
            var parts = rel.Split('/');
            for (int i = 0; i < parts.Length - 1; i++)   // 最後一段是檔名,跳過
            {
                var seg = parts[i];
                if (seg.Length == 0 || ruled.Contains(seg)) continue;
                count[seg] = count.GetValueOrDefault(seg) + 1;
                sample.TryAdd(seg, rel);
            }
        }

        return count
            .Select(kv => new PendingSegment(kv.Key, kv.Value, sample[kv.Key], PathTagDefaults.Suggest(kv.Key)))
            .OrderByDescending(p => p.Count)
            .ToList();
    }
}
```

- [ ] **Step 3: 寫失敗的測試**

Create `tests/Pm.Scanner.Tests/PathTagDefaultsTests.cs`:

```csharp
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class PathTagDefaultsTests
{
    [Theory]
    [InlineData("我不知道", "ignore")]
    [InlineData("2024", "meta_year")]
    [InlineData("2023", "meta_year")]
    [InlineData("vspo", "map_to_tag")]
    [InlineData("12", "map_to_tag")]      // 非四位數不算年份
    public void Suggest_maps_segment_to_action(string segment, string expected)
        => Assert.Equal(expected, PathTagDefaults.Suggest(segment));
}
```

Create `tests/Pm.Scanner.Tests/PathTagServiceTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class PathTagServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-pathtag-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public PathTagServiceTests()
    {
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    private async Task<long> SeedLocations(params string[] relPaths)
    {
        await using var ctx = NewContext();
        var root = new LibraryRoot { Name = "t", AbsPath = @"D:\x" };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        int i = 0;
        foreach (var rel in relPaths)
        {
            var photo = new Photo { FileHash = new string((char)('a' + i++), 64), FileSize = 1 };
            photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = rel });
            ctx.Photos.Add(photo);
        }
        await ctx.SaveChangesAsync();
        return root.Id;
    }

    [Fact]
    public async Task Lists_directory_segments_with_counts_and_suggestions()
    {
        var rootId = await SeedLocations("vspo/a.png", "vspo/b.png", "2434/vspo/c.png", "我不知道/d.png");

        await using var ctx = NewContext();
        var pending = await new PathTagService(ctx).GetPendingSegmentsAsync(rootId);

        var vspo = pending.Single(p => p.Segment == "vspo");
        Assert.Equal(3, vspo.Count);
        Assert.Equal("map_to_tag", vspo.SuggestedAction);
        Assert.Equal("ignore", pending.Single(p => p.Segment == "我不知道").SuggestedAction);
        Assert.Equal("map_to_tag", pending.Single(p => p.Segment == "2434").SuggestedAction);
        Assert.DoesNotContain(pending, p => p.Segment.EndsWith(".png"));   // 檔名不算段
    }
}
```

- [ ] **Step 4: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter "PathTagDefaultsTests|PathTagServiceTests"
```

Expected: PASS(5 Theory + 1 = 6)。

- [ ] **Step 5: Commit**

```bash
cd /d/picture-management
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: 路徑段待確認收集 + 預設動作建議(我不知道→ignore、年份→meta_year)"
```

---

## Task 2: 確認規則 → 寫 path_tag_rule + 套用 photo_tag

確認一個段的動作:寫(或更新)`path_tag_rule`;map_to_tag/meta_year 時建立(或取用)tag,並把該段下所有照片打上 `photo_tag(source='path')`。

**Files:**
- Modify: `src/Pm.Scanner/PathTagService.cs`
- Modify: `tests/Pm.Scanner.Tests/PathTagServiceTests.cs`

**Interfaces:**
- Produces:`PathTagService.ApplyRuleAsync(long? rootId, string segment, string action, string? tagName, CancellationToken ct = default) -> Task`(`rootId=null` 表全域;`action ∈ map_to_tag|ignore|meta_year`)。

- [ ] **Step 1: 加 ApplyRuleAsync 與內部套用**

在 `src/Pm.Scanner/PathTagService.cs` 類別內加:

```csharp
    public async Task ApplyRuleAsync(
        long? rootId, string segment, string action, string? tagName, CancellationToken ct = default)
    {
        long? tagId = null;
        if (action is "map_to_tag" or "meta_year")
        {
            var name = action == "meta_year" ? segment : (tagName ?? segment);
            var kind = action == "meta_year" ? "meta" : "path";
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is null)
            {
                tag = new Tag { Name = name, Kind = kind };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(ct);
            }
            tagId = tag.Id;
        }

        var rule = await db.PathTagRules
            .FirstOrDefaultAsync(r => r.LibraryRootId == rootId && r.Segment == segment, ct);
        if (rule is null)
            db.PathTagRules.Add(new PathTagRule { LibraryRootId = rootId, Segment = segment, Action = action, TagId = tagId });
        else { rule.Action = action; rule.TagId = tagId; }
        await db.SaveChangesAsync(ct);

        if (tagId is not null)
            await ApplySegmentTagAsync(rootId, segment, tagId.Value, ct);
    }

    private async Task ApplySegmentTagAsync(long? rootId, string segment, long tagId, CancellationToken ct)
    {
        var pat = segment;
        var photoIds = await db.PhotoLocations
            .Where(l => (rootId == null || l.LibraryRootId == rootId)
                        && (l.RelPath.StartsWith(pat + "/") || l.RelPath.Contains("/" + pat + "/")))
            .Select(l => l.PhotoId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var pid in photoIds)
            if (!await db.PhotoTags.AnyAsync(pt => pt.PhotoId == pid && pt.TagId == tagId, ct))
                db.PhotoTags.Add(new PhotoTag { PhotoId = pid, TagId = tagId, Source = "path" });

        await db.SaveChangesAsync(ct);
    }
```

- [ ] **Step 2: 寫失敗的測試**

在 `tests/Pm.Scanner.Tests/PathTagServiceTests.cs` 類別內加:

```csharp
    [Fact]
    public async Task Apply_map_to_tag_creates_tag_and_tags_photos()
    {
        var rootId = await SeedLocations("vspo/a.png", "vspo/sub/b.png", "other/c.png");

        await using (var ctx = NewContext())
            await new PathTagService(ctx).ApplyRuleAsync(rootId, "vspo", "map_to_tag", tagName: "vspo");

        await using var verify = NewContext();
        var tag = await verify.Tags.SingleAsync(t => t.Name == "vspo");
        Assert.Equal("path", tag.Kind);
        Assert.Equal(2, await verify.PhotoTags.CountAsync(pt => pt.TagId == tag.Id && pt.Source == "path"));
        Assert.False(await verify.PathTagRules
            .AnyAsync(r => r.Segment == "vspo" && r.Action != "map_to_tag"));
    }

    [Fact]
    public async Task Apply_ignore_records_rule_without_tag()
    {
        var rootId = await SeedLocations("我不知道/a.png");

        await using (var ctx = NewContext())
            await new PathTagService(ctx).ApplyRuleAsync(rootId, "我不知道", "ignore", null);

        await using var verify = NewContext();
        Assert.Equal("ignore", (await verify.PathTagRules.SingleAsync()).Action);
        Assert.Equal(0, await verify.PhotoTags.CountAsync());

        // 確認後不再列入待確認
        await using var ctx2 = NewContext();
        var pending = await new PathTagService(ctx2).GetPendingSegmentsAsync(rootId);
        Assert.DoesNotContain(pending, p => p.Segment == "我不知道");
    }

    [Fact]
    public async Task Apply_meta_year_creates_meta_kind_tag()
    {
        var rootId = await SeedLocations("2024/a.png");

        await using (var ctx = NewContext())
            await new PathTagService(ctx).ApplyRuleAsync(rootId, "2024", "meta_year", null);

        await using var verify = NewContext();
        var tag = await verify.Tags.SingleAsync(t => t.Name == "2024");
        Assert.Equal("meta", tag.Kind);
        Assert.Equal(1, await verify.PhotoTags.CountAsync(pt => pt.TagId == tag.Id));
    }
```

- [ ] **Step 3: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter PathTagServiceTests
```

Expected: PASS(收集 1 + 本 task 3 = 4)。

- [ ] **Step 4: Commit**

```bash
cd /d/picture-management
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: 確認路徑規則→寫 path_tag_rule + 套 photo_tag(source=path)"
```

---

## Task 3: 套用既有規則(重掃後自動補)

掃描新增照片後,把**已確認過的規則**套到新照片,不必再問。

**Files:**
- Modify: `src/Pm.Scanner/PathTagService.cs`
- Modify: `tests/Pm.Scanner.Tests/PathTagServiceTests.cs`

**Interfaces:**
- Produces:`PathTagService.ApplyExistingRulesAsync(long rootId, CancellationToken ct = default) -> Task<int>`(回套用的規則數)。

- [ ] **Step 1: 加 ApplyExistingRulesAsync**

在 `src/Pm.Scanner/PathTagService.cs` 類別內加:

```csharp
    public async Task<int> ApplyExistingRulesAsync(long rootId, CancellationToken ct = default)
    {
        var rules = await db.PathTagRules
            .Where(r => (r.LibraryRootId == rootId || r.LibraryRootId == null) && r.TagId != null)
            .ToListAsync(ct);

        foreach (var r in rules)
            await ApplySegmentTagAsync(r.LibraryRootId, r.Segment, r.TagId!.Value, ct);

        return rules.Count;
    }
```

- [ ] **Step 2: 寫失敗的測試**

在 `tests/Pm.Scanner.Tests/PathTagServiceTests.cs` 加:

```csharp
    [Fact]
    public async Task Existing_rules_apply_to_newly_added_photos()
    {
        var rootId = await SeedLocations("vspo/a.png");
        await using (var ctx = NewContext())
            await new PathTagService(ctx).ApplyRuleAsync(rootId, "vspo", "map_to_tag", "vspo");

        // 之後又進來一張同段新照片
        await using (var ctx = NewContext())
        {
            var root = await ctx.LibraryRoots.FindAsync(rootId);
            var photo = new Photo { FileHash = new string('z', 64), FileSize = 1 };
            photo.Locations.Add(new PhotoLocation { LibraryRoot = root!, RelPath = "vspo/new.png" });
            ctx.Photos.Add(photo);
            await ctx.SaveChangesAsync();
        }

        int applied;
        await using (var ctx = NewContext())
            applied = await new PathTagService(ctx).ApplyExistingRulesAsync(rootId);

        Assert.Equal(1, applied);
        await using var verify = NewContext();
        var tag = await verify.Tags.SingleAsync(t => t.Name == "vspo");
        Assert.Equal(2, await verify.PhotoTags.CountAsync(pt => pt.TagId == tag.Id));   // 舊 + 新
    }
```

- [ ] **Step 3: 跑測試 + Commit**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter PathTagServiceTests
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: 套用既有路徑規則到新照片(重掃免再問)"
```

Expected: PASS(5)。

---

## Task 4: API 端點(待確認 / 確認 / 套用)

**Files:**
- Modify: `src/Pm.Api/Program.cs`
- Create: `tests/Pm.Api.Tests/PathTagApiTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/roots/{id:long}/pending-segments` → `200` `PendingSegment[]`
  - `POST /api/path-rules`,body `{ rootId?: long, segment: string, action: string, tagName?: string }` → `200`
  - `POST /api/roots/{id:long}/apply-path-tags` → `200` `{ rulesApplied: int }`

- [ ] **Step 1: 註冊 service + 端點**

在 `src/Pm.Api/Program.cs` 服務註冊區加:

```csharp
builder.Services.AddScoped<PathTagService>();
```

端點區加:

```csharp
app.MapGet("/api/roots/{id:long}/pending-segments", async (long id, PathTagService svc) =>
    Results.Ok(await svc.GetPendingSegmentsAsync(id)));

app.MapPost("/api/path-rules", async (PathRuleDto dto, PathTagService svc) =>
{
    await svc.ApplyRuleAsync(dto.RootId, dto.Segment, dto.Action, dto.TagName);
    return Results.Ok();
});

app.MapPost("/api/roots/{id:long}/apply-path-tags", async (long id, PathTagService svc) =>
    Results.Ok(new { rulesApplied = await svc.ApplyExistingRulesAsync(id) }));
```

DTO(置於檔末 `public partial class Program { }` 之前):

```csharp
public record PathRuleDto(long? RootId, string Segment, string Action, string? TagName);
```

- [ ] **Step 2: 寫失敗的端點測試**

Create `tests/Pm.Api.Tests/PathTagApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Api.Tests;

public class PathTagApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-ptapi-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-ptroot-{Guid.NewGuid():N}");
    private readonly string _thumbs = Path.Combine(Path.GetTempPath(), $"pm-ptthumbs-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public PathTagApiTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "vspo"));
        var db = _dbPath; var th = _thumbs;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={db};Foreign Keys=True",
                    ["Thumbnails:Dir"] = th
                })));
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        foreach (var d in new[] { _root, _thumbs }) if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    private record RootCreated(long Id);

    [Fact]
    public async Task Pending_then_confirm_tags_photos()
    {
        using (var img = new Image<Rgba32>(20, 20))
            await img.SaveAsPngAsync(Path.Combine(_root, "vspo", "a.png"));

        var client = _factory.CreateClient();
        var root = await (await client.PostAsJsonAsync("/api/roots", new { name = "t", absPath = _root }))
            .Content.ReadFromJsonAsync<RootCreated>();
        await client.PostAsync($"/api/roots/{root!.Id}/scan", null);

        var pending = await client.GetStringAsync($"/api/roots/{root.Id}/pending-segments");
        Assert.Contains("vspo", pending);

        var confirm = await client.PostAsJsonAsync("/api/path-rules",
            new { rootId = root.Id, segment = "vspo", action = "map_to_tag", tagName = "vspo" });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        // 確認後 vspo 不再待確認
        var pending2 = await client.GetStringAsync($"/api/roots/{root.Id}/pending-segments");
        Assert.DoesNotContain("\"vspo\"", pending2);
    }
}
```

- [ ] **Step 3: 跑測試 + 全 solution 驗收**

Run:

```bash
cd /d/picture-management
dotnet test
```

Expected: 全綠。本計畫新增:Scanner.Tests +6(Defaults 5+Service 收集 1… 實為 Defaults 1 類含 5 case + Service 5 case)、Api.Tests +1。累計約 **42 passed**。

- [ ] **Step 4: Commit**

```bash
cd /d/picture-management
git add src/Pm.Api tests/Pm.Api.Tests
git commit -m "feat: 路徑→tag 端點(待確認段/確認規則/套用既有規則)"
```

---

## 完成定義(路徑→tag 確認)

- `GET /api/roots/{id}/pending-segments` 列出沒見過的目錄段(次數/範例/建議),已有規則者不再出現。
- `POST /api/path-rules` 確認動作:寫 `path_tag_rule`(每段唯一),map_to_tag/meta_year 建 tag 並打 `photo_tag(source='path')`,ignore 只記規則。
- `POST /api/roots/{id}/apply-path-tags` 把既有規則套到新照片。
- 預設:`我不知道`→ignore、四位數年份→meta_year。
- `dotnet test` 全綠。

**明確不在本計畫:** 掃描流程自動串接 apply(目前由 API 手動觸發;背景自動化後續)、tag 階層(`tag_relation`)的建立 UI(計畫 6)。

---

## Self-Review 註記

- **Spec 覆蓋:** §5.4 學習型路徑→tag、§4.2 `path_tag_rule`(每段唯一、action 三態)、tag.kind(path/meta)、`photo_tag.source='path'`、內建預設(我不知道/年份)。
- **無 placeholder / 型別一致:** `PendingSegment`、`ApplyRuleAsync`/`ApplyExistingRulesAsync` 簽章、端點 DTO 一致。
```
