# 資料層作品軸 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WD14 角色 tag 字串裡夾的作品(`aris_(blue_archive)` 的 `blue_archive`)在後端 ingest 解析成獨立 copyright tag + 寫 `tag_relation` 邊,讓側欄 facet 樹有真實「作品→角色」階層,並能用既有 closure 搜整個作品;前端側欄分流(只收 copyright+character)+ 分區整段收折。

**Architecture:** 純函式 `CopyrightAxis.ParseWork`(移植前端 `parseCharacter` 的作品判定)→ `CopyrightAxisService.SeedFromCharacterAsync`(upsert copyright tag + 冪等寫邊 + 防環)→ 接進 `TaggingWorker`(即時)與 `POST /api/maintenance/copyright-axis/rebuild`(backfill)。`TagFacetService` 加 kind 過濾 + copyright 父節點聚合 count。前端 `facet-sidebar` 分區收折 + localStorage + rootless 改名 + 年份 tooltip。搜尋側零改(`PhotoQueryService` 已用 `closure.DescendantsAsync`)。

**Tech Stack:** .NET 10(Pm.Scanner / Pm.Api)、EF Core + SQLite、xUnit;Angular(Pm.Web)+ `ng test`/`ng build`。

## Global Constraints

- TargetFramework `net10.0`;Nullable + ImplicitUsings enable。後端 TDD(temp SQLite),前端純函式 `ng test` + UI 改完 `ng build` 手測。
- **canonical 不動**:character tag 原名 `aris_(blue_archive)` 照存;本功能只「新增」copyright tag + `tag_relation` 邊,不改名/不刪既有 tag(守鐵則 #3/#5、延續顯示層 v1)。
- **copyright tag canonical 用底線原值**(`blue_archive`,非 `blue archive`),與 booru canonical 慣例一致;中文/空白屬顯示層。
- **作品判定與前端一致**:移植 `src/Pm.Web/src/app/core/tag-display.ts` 的 `parseCharacter` work 判定 —— 反覆剝離尾端 `_(...)`,**最右側「非黑名單」群組 = 作品**;黑名單 `NON_WORK_SUFFIX`(male/female/young/old/aged_up/child/teenage/adult/alternate/cosplay/ghost/human/beast)同一份語意。畸形(括號前無名)或全黑名單 → 無作品。
- **冪等 + 防呆**(採 danbooru/Hydrus 規則):邊 (parent,child) 唯一不重插;防自我(parent==child);防環(child 的後代已含 parent 則不加)。
- **無 tag/edge source 欄位**(schema 現況):`TagRelation` 只有 `ParentTagId/ChildTagId`,`Tag` 無 Source。故衍生 copyright 以 `Kind == "copyright"` 識別;人工覆寫 = 標籤庫刪/改該 tag 或邊(已有 `TagService.Delete/Update/Merge`)。**spec §3.2「邊記 source=wd14」以此取代**(schema 不支援,且 v1 不需要)。
- **搜尋側零改**:`PhotoQueryService`(`:20/:27/:56/:63`)已對 include/exclude 呼叫 `closure.DescendantsAsync`;寫邊即生效。本計畫不改 PhotoQueryService,僅加整合測試(Task 7)。
- **facet count**:copyright 父節點直接無圖,count = 直接子節點 count 總和(spec §3.5 (ii) 近似,facet 顯示非精度關鍵、零額外 query;spec 已容許)。

---

### Task 1: `CopyrightAxis.ParseWork` 純函式

**Files:**
- Create: `src/Pm.Scanner/CopyrightAxis.cs`
- Test: `tests/Pm.Scanner.Tests/CopyrightAxisTests.cs`

**Interfaces:**
- Produces:`public static class CopyrightAxis` 內 `public static string? ParseWork(string canonical)`

- [ ] **Step 1: 寫失敗測試**

`tests/Pm.Scanner.Tests/CopyrightAxisTests.cs`:

```csharp
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class CopyrightAxisTests
{
    [Theory]
    [InlineData("aris_(blue_archive)", "blue_archive")]       // 單作品
    [InlineData("jeanne_d'arc_(alter)_(fate)", "fate")]       // 最右非黑名單=作品,alter 為造型
    [InlineData("hoshino_(blue_archive)", "blue_archive")]
    public void Extracts_rightmost_non_blacklist_group_as_work(string name, string work)
        => Assert.Equal(work, CopyrightAxis.ParseWork(name));

    [Theory]
    [InlineData("long_hair")]              // 無括號
    [InlineData("aris_(cosplay)")]         // 全黑名單(cosplay)
    [InlineData("someone_(male)")]         // 全黑名單(male)
    [InlineData("_(foo)")]                 // 畸形:括號前無名
    [InlineData("")]                       // 空
    public void Returns_null_when_no_work(string name)
        => Assert.Null(CopyrightAxis.ParseWork(name));
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter "FullyQualifiedName~CopyrightAxisTests"`
Expected: 編譯失敗 —— `CopyrightAxis` 不存在。

- [ ] **Step 3: 寫實作**

`src/Pm.Scanner/CopyrightAxis.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Pm.Scanner;

// 從 WD14 character canonical 解析「作品(copyright)」。
// 與前端 tag-display.ts parseCharacter 的 work 判定一致:反覆剝離尾端 _(...),
// 最右側「非黑名單」群組 = 作品;畸形或全黑名單 → null。canonical 不變,本層只讀。
public static partial class CopyrightAxis
{
    // 限定詞黑名單(命中則歸造型/性別等,非作品)。與前端 NON_WORK_SUFFIX 同一份語意。
    private static readonly HashSet<string> NonWorkSuffix = new(StringComparer.Ordinal)
    {
        "male", "female", "young", "old", "aged_up", "child", "teenage", "adult",
        "alternate", "cosplay", "ghost", "human", "beast",
    };

    [GeneratedRegex(@"_\(([^()]*)\)$")]
    private static partial Regex SuffixRe();

    public static string? ParseWork(string canonical)
    {
        var rest = canonical ?? string.Empty;
        var groups = new List<string>();
        Match m;
        while ((m = SuffixRe().Match(rest)).Success)
        {
            groups.Insert(0, m.Groups[1].Value);   // 還原由左到右
            rest = rest[..m.Index];
        }
        if (rest.Length == 0) return null;          // 畸形:括號前無角色名
        for (var i = groups.Count - 1; i >= 0; i--)
            if (!NonWorkSuffix.Contains(groups[i]))
                return groups[i];                    // 最右側非黑名單 = 作品
        return null;                                 // 無括號 / 全黑名單
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter "FullyQualifiedName~CopyrightAxisTests"`
Expected: PASS（8 cases）。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Scanner/CopyrightAxis.cs tests/Pm.Scanner.Tests/CopyrightAxisTests.cs
git commit -m "feat(scanner): CopyrightAxis.ParseWork 解析 WD14 角色標的作品(移植前端判定)"
```

---

### Task 2: `CopyrightAxisService.SeedFromCharacterAsync`

**Files:**
- Create: `src/Pm.Scanner/CopyrightAxisService.cs`
- Test: `tests/Pm.Scanner.Tests/CopyrightAxisServiceTests.cs`

**Interfaces:**
- Consumes:`TagService.UpsertByNameAsync(string, string, ct) -> Task<Tag>`、`TagClosureService.DescendantsAsync(long, ct) -> Task<List<long>>`、`PmDbContext.TagRelations`、`CopyrightAxis.ParseWork`。
- Produces:`public sealed class CopyrightAxisService(PmDbContext db, TagService tags, TagClosureService closure)` 內 `public Task<bool> SeedFromCharacterAsync(Tag characterTag, CancellationToken ct = default)`(回是否新增了邊)。

- [ ] **Step 1: 寫失敗測試**

`tests/Pm.Scanner.Tests/CopyrightAxisServiceTests.cs`(沿用既有 Scanner 測試的 temp SQLite + `ctx.Database.Migrate()` 模式;參照 `ReconcileTests` 建 context):

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class CopyrightAxisServiceTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pm-cax-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_db};Foreign Keys=True";
    private PmDbContext NewContext() => new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    public CopyrightAxisServiceTests()
    {
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    private CopyrightAxisService Svc(PmDbContext ctx) => new(ctx, new TagService(ctx), new TagClosureService(ctx));

    [Fact]
    public async Task Seeds_copyright_tag_and_edge_from_character()
    {
        using var ctx = NewContext();
        var character = await new TagService(ctx).UpsertByNameAsync("aris_(blue_archive)", "character");

        var added = await Svc(ctx).SeedFromCharacterAsync(character);

        Assert.True(added);
        var copyright = await ctx.Tags.FirstAsync(t => t.Name == "blue_archive");
        Assert.Equal("copyright", copyright.Kind);
        Assert.True(await ctx.TagRelations.AnyAsync(r => r.ParentTagId == copyright.Id && r.ChildTagId == character.Id));
    }

    [Fact]
    public async Task Is_idempotent_no_duplicate_edge()
    {
        using var ctx = NewContext();
        var character = await new TagService(ctx).UpsertByNameAsync("aris_(blue_archive)", "character");
        await Svc(ctx).SeedFromCharacterAsync(character);

        var addedAgain = await Svc(ctx).SeedFromCharacterAsync(character);

        Assert.False(addedAgain);
        Assert.Equal(1, await ctx.TagRelations.CountAsync());
    }

    [Fact]
    public async Task No_work_no_edge()
    {
        using var ctx = NewContext();
        var character = await new TagService(ctx).UpsertByNameAsync("long_hair", "character");

        Assert.False(await Svc(ctx).SeedFromCharacterAsync(character));
        Assert.Equal(0, await ctx.TagRelations.CountAsync());
    }

    [Fact]
    public async Task Non_character_kind_ignored()
    {
        using var ctx = NewContext();
        var general = await new TagService(ctx).UpsertByNameAsync("blue_archive", "general");

        Assert.False(await Svc(ctx).SeedFromCharacterAsync(general));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter "FullyQualifiedName~CopyrightAxisServiceTests"`
Expected: 編譯失敗 —— `CopyrightAxisService` 不存在。

- [ ] **Step 3: 寫實作**

`src/Pm.Scanner/CopyrightAxisService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Scanner;

// 從 WD14 character tag 拆作品:upsert copyright tag + 冪等寫 parent(copyright)→child(character) 邊。
// canonical 不動;衍生 copyright 以 Kind=copyright 識別(schema 無 source 欄)。供 TaggingWorker 即時 + backfill 共用。
public sealed class CopyrightAxisService(PmDbContext db, TagService tags, TagClosureService closure)
{
    // 回是否新增了邊(供 backfill 統計)。非 character / 無作品 / 已存在 / 成環 → false。
    public async Task<bool> SeedFromCharacterAsync(Tag characterTag, CancellationToken ct = default)
    {
        if (characterTag.Kind != "character") return false;
        var work = CopyrightAxis.ParseWork(characterTag.Name);
        if (work is null) return false;

        var copyright = await tags.UpsertByNameAsync(work, "copyright", ct);
        if (copyright.Id == characterTag.Id) return false;   // 防自我

        var exists = await db.TagRelations.AnyAsync(
            r => r.ParentTagId == copyright.Id && r.ChildTagId == characterTag.Id, ct);
        if (exists) return false;

        // 防環:copyright 已是 character 的後代 → 加 copyright→character 會成環,跳過。
        var childDescendants = await closure.DescendantsAsync(characterTag.Id, ct);
        if (childDescendants.Contains(copyright.Id)) return false;

        db.TagRelations.Add(new TagRelation { ParentTagId = copyright.Id, ChildTagId = characterTag.Id });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter "FullyQualifiedName~CopyrightAxisServiceTests"`
Expected: PASS（4 cases）。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Scanner/CopyrightAxisService.cs tests/Pm.Scanner.Tests/CopyrightAxisServiceTests.cs
git commit -m "feat(scanner): CopyrightAxisService 拆作品 + 冪等寫 tag_relation 邊"
```

---

### Task 3: 接進 TaggingWorker(即時拆作品)+ DI 註冊

**Files:**
- Modify: `src/Pm.Api/TaggingWorker.cs`(`ProcessNextAsync` 簽章 + 迴圈、`ExecuteAsync` 解析 service)
- Modify: `src/Pm.Api/Program.cs`(DI 註冊 `CopyrightAxisService`)
- Test: `tests/Pm.Api.Tests/TaggingWorkerTests.cs`(既有檔,加一條斷言)

**Interfaces:**
- Consumes:`CopyrightAxisService.SeedFromCharacterAsync(Tag, ct)`(Task 2)。

- [ ] **Step 1: DI 註冊**

`src/Pm.Api/Program.cs`,在既有 `builder.Services.AddScoped<TagService>();` 附近加:

```csharp
builder.Services.AddScoped<CopyrightAxisService>();
```

（`CopyrightAxisService` 在 `Pm.Scanner` 命名空間;`Program.cs` 已 `using Pm.Scanner;`。其相依 `TagClosureService` 既有已註冊。）

- [ ] **Step 2: 改 TaggingWorker 接線**

`src/Pm.Api/TaggingWorker.cs`:

在 `ExecuteAsync` 的 while 迴圈內,把:
```csharp
            var tagSvc = scope.ServiceProvider.GetRequiredService<TagService>();
            var processed = await ProcessNextAsync(db, tagSvc, ct);
```
改成:
```csharp
            var tagSvc = scope.ServiceProvider.GetRequiredService<TagService>();
            var copyrightAxis = scope.ServiceProvider.GetRequiredService<CopyrightAxisService>();
            var processed = await ProcessNextAsync(db, tagSvc, copyrightAxis, ct);
```

把 `ProcessNextAsync` 簽章與標註迴圈改成:
```csharp
    public async Task<bool> ProcessNextAsync(PmDbContext db, TagService tagSvc, CopyrightAxisService copyrightAxis, CancellationToken ct)
```
迴圈內:
```csharp
            foreach (var (name, kind, conf) in await tagger.TagAsync(path, ct))
            {
                var tag = await tagSvc.UpsertByNameAsync(name, kind, ct);
                await tagSvc.AttachTagAsync(job.PhotoId, tag.Id, "wd14", conf, existing, ct);
                if (kind == "character")
                    await copyrightAxis.SeedFromCharacterAsync(tag, ct);   // 拆作品 + 寫邊(冪等)
            }
```

並把 `TaggingWorker.cs` 頂部 using 補 `using Pm.Scanner;`(若尚無;`CopyrightAxisService` 所在)。

- [ ] **Step 3: 既有測試加斷言(讓 stub tagger 回傳 character 標)**

開啟既有 `tests/Pm.Api.Tests/TaggingWorkerTests.cs`,找到「處理一個 WD14 job → 驗證 photo_tag 建立」的測試(它已有 stub `IWd14Tagger` 與 seed photo+location+job 的設置)。**沿用該測試的既有 setup**,把 stub tagger 回傳的其中一個 tag 設為 `("aris_(blue_archive)", "character", 0.9f)`(或在既有回傳清單追加此筆),並在呼叫 `ProcessNextAsync(...)`(注意新增 `copyrightAxis` 參數,從測試的 `ServiceProvider`/手建 `new CopyrightAxisService(db, new TagService(db), new TagClosureService(db))` 取得)後,加斷言:

```csharp
// 拆作品:character 標 aris_(blue_archive) → 建 copyright tag blue_archive + tag_relation 邊
var copyright = await db.Tags.FirstAsync(t => t.Name == "blue_archive");
Assert.Equal("copyright", copyright.Kind);
var character = await db.Tags.FirstAsync(t => t.Name == "aris_(blue_archive)");
Assert.True(await db.TagRelations.AnyAsync(r => r.ParentTagId == copyright.Id && r.ChildTagId == character.Id));
```

若既有測試以 DI scope 取得 `ProcessNextAsync` 依賴,改以同 scope `GetRequiredService<CopyrightAxisService>()` 傳入。**不改測試原有意圖,只擴充作品軸斷言。**

- [ ] **Step 4: build + 相關測試**

Run: `dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter "FullyQualifiedName~TaggingWorkerTests"`
Expected: PASS（含新作品軸斷言）。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Api/TaggingWorker.cs src/Pm.Api/Program.cs tests/Pm.Api.Tests/TaggingWorkerTests.cs
git commit -m "feat(api): TaggingWorker 即時拆作品(character→copyright 邊)+ DI 註冊"
```

---

### Task 4: backfill 維護端點

**Files:**
- Modify: `src/Pm.Api/Program.cs`(加 `.WithTags("Maintenance")` 端點)
- Test: `tests/Pm.Api.Tests/CopyrightAxisApiTests.cs`(新增)

**Interfaces:**
- Produces:`POST /api/maintenance/copyright-axis/rebuild` → `200 { scanned: int, edgesCreated: int }`

- [ ] **Step 1: 寫失敗測試**

`tests/Pm.Api.Tests/CopyrightAxisApiTests.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pm.Data;
using Pm.Scanner;
using Xunit;

namespace Pm.Api.Tests;

public class CopyrightAxisApiTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pm-caxapi-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public CopyrightAxisApiTests()
    {
        var db = _db;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={db};Foreign Keys=True"
                })));
    }

    [Fact]
    public async Task Rebuild_creates_copyright_tags_and_edges_idempotently()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<TagService>();
            await tags.UpsertByNameAsync("aris_(blue_archive)", "character");
            await tags.UpsertByNameAsync("hoshino_(blue_archive)", "character");
            await tags.UpsertByNameAsync("long_hair", "general");   // 不該長出邊
        }
        var client = _factory.CreateClient();

        var res = await client.PostAsync("/api/maintenance/copyright-axis/rebuild", null);
        var body = await res.Content.ReadFromJsonAsync<Rebuild>();

        Assert.NotNull(body);
        Assert.Equal(2, body!.EdgesCreated);     // 兩個角色各一條邊
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            Assert.True(ctx.Tags.Any(t => t.Name == "blue_archive" && t.Kind == "copyright"));
            Assert.Equal(2, ctx.TagRelations.Count());
        }

        // 冪等:再跑一次不新增
        var res2 = await client.PostAsync("/api/maintenance/copyright-axis/rebuild", null);
        var body2 = await res2.Content.ReadFromJsonAsync<Rebuild>();
        Assert.Equal(0, body2!.EdgesCreated);
    }

    private sealed record Rebuild(int Scanned, int EdgesCreated);

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter "FullyQualifiedName~CopyrightAxisApiTests"`
Expected: FAIL（端點不存在 → 404）。

- [ ] **Step 3: 加 backfill 端點**

`src/Pm.Api/Program.cs`,在 Maintenance 區(或 Tags 區後)加:

```csharp
// 維護:對所有現有 character tag 補拆作品 + 寫 tag_relation 邊(冪等,可重跑)。
app.MapPost("/api/maintenance/copyright-axis/rebuild", async (PmDbContext db, CopyrightAxisService axis) =>
{
    var characters = await db.Tags.Where(t => t.Kind == "character").ToListAsync();
    var edgesCreated = 0;
    foreach (var c in characters)
        if (await axis.SeedFromCharacterAsync(c)) edgesCreated++;
    return Results.Ok(new { scanned = characters.Count, edgesCreated });
})
    .WithTags("Maintenance");
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter "FullyQualifiedName~CopyrightAxisApiTests"`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Api/Program.cs tests/Pm.Api.Tests/CopyrightAxisApiTests.cs
git commit -m "feat(api): copyright-axis backfill 維護端點(冪等)"
```

---

### Task 5: TagFacetService kind 分流 + copyright 聚合 count

**Files:**
- Modify: `src/Pm.Scanner/TagFacetService.cs`
- Test: `tests/Pm.Scanner.Tests/TagFacetCopyrightTests.cs`(新增)

**Interfaces:**
- 既有 `TagFacetService.BuildAsync()` → `FacetTree(Tree, Rootless, General, Meta)`;`FacetNode(Name, Kind, Count, Multi, Children)` 不變。

- [ ] **Step 1: 寫失敗測試**

`tests/Pm.Scanner.Tests/TagFacetCopyrightTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class TagFacetCopyrightTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pm-facet-{Guid.NewGuid():N}.sqlite");
    private PmDbContext NewContext() => new(new DbContextOptionsBuilder<PmDbContext>()
        .UseSqlite($"Data Source={_db};Foreign Keys=True").Options);

    public TagFacetCopyrightTests() { using var c = NewContext(); c.Database.Migrate(); }

    [Fact]
    public async Task Tree_only_copyright_and_character_general_excluded()
    {
        using var ctx = NewContext();
        // 作品 → 角色 + 一個 present photo 掛角色;外加一個 general tag(不該進 tree/rootless)
        var root = new LibraryRoot { Name = "r", AbsPath = @"C:\x" }; ctx.LibraryRoots.Add(root);
        var copyright = new Tag { Name = "blue_archive", Kind = "copyright" };
        var character = new Tag { Name = "aris_(blue_archive)", Kind = "character" };
        var general = new Tag { Name = "long_hair", Kind = "general" };
        ctx.Tags.AddRange(copyright, character, general);
        var photo = new Photo { FileHash = new string('a', 64), ImportedAt = DateTimeOffset.UtcNow };
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
        ctx.PhotoLocations.Add(new PhotoLocation { PhotoId = photo.Id, LibraryRootId = root.Id, RelPath = "a.png", Status = "present", FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow });
        ctx.PhotoTags.Add(new PhotoTag { PhotoId = photo.Id, TagId = character.Id, Source = "wd14" });
        ctx.PhotoTags.Add(new PhotoTag { PhotoId = photo.Id, TagId = general.Id, Source = "wd14" });
        ctx.TagRelations.Add(new TagRelation { ParentTagId = copyright.Id, ChildTagId = character.Id });
        await ctx.SaveChangesAsync();

        var tree = await new TagFacetService(ctx).BuildAsync();

        // 樹頂只有 copyright,其下為 character;general 不在 tree/rootless
        var top = Assert.Single(tree.Tree);
        Assert.Equal("blue_archive", top.Name);
        Assert.Equal("copyright", top.Kind);
        Assert.Equal(1, top.Count);                       // copyright 聚合 = 子角色 count 總和
        var child = Assert.Single(top.Children!);
        Assert.Equal("aris_(blue_archive)", child.Name);
        Assert.DoesNotContain(tree.Rootless, n => n.Kind == "general");
        Assert.DoesNotContain(tree.Tree, n => n.Kind == "general");
        Assert.Contains(tree.General, g => g.Name == "long_hair");   // general 仍在專屬區
    }

    [Fact]
    public async Task Character_without_copyright_goes_rootless()
    {
        using var ctx = NewContext();
        ctx.Tags.Add(new Tag { Name = "solo_character", Kind = "character" });
        await ctx.SaveChangesAsync();

        var tree = await new TagFacetService(ctx).BuildAsync();

        Assert.Contains(tree.Rootless, n => n.Name == "solo_character" && n.Kind == "character");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter "FullyQualifiedName~TagFacetCopyrightTests"`
Expected: FAIL —— 目前 general 會進 rootless、copyright count=0(直接無圖)。

- [ ] **Step 3: 改 TagFacetService**

`src/Pm.Scanner/TagFacetService.cs`:

(a) `Build` 區域函式:copyright 節點 count 改為子節點聚合。把:
```csharp
            return new FacetNode(t.Name, t.Kind, CountFor(id), MultiFor(id), kids);
```
改成:
```csharp
            var count = CountFor(id);
            if (t.Kind == "copyright" && kids is not null)
                count = kids.Sum(k => k.Count);   // copyright 直接無圖,以子角色 count 聚合(facet 顯示用近似)
            return new FacetNode(t.Name, t.Kind, count, MultiFor(id), kids);
```

(b) root/rootless 迴圈:只收 copyright + character。把:
```csharp
        foreach (var t in tags)
        {
            if (hasParent.Contains(t.Id)) continue;   // 非頂層
```
改成:
```csharp
        foreach (var t in tags)
        {
            if (t.Kind != "copyright" && t.Kind != "character") continue;   // 樹只收作品/角色;general/meta 各有專屬區
            if (hasParent.Contains(t.Id)) continue;   // 非頂層
```

- [ ] **Step 4: 跑測試確認通過 + 全 Scanner 測試**

Run: `dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj`
Expected: 新測 PASS;既有 facet 相關測試若因「不再把 general 放 tree/rootless」而需調整,依新語意更新斷言(general/meta 改在 `tree.General`/`tree.Meta` 驗)。**若既有測試斷言舊行為(general 在 rootless),那是預期的語意變更,更新它。**

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Scanner/TagFacetService.cs tests/Pm.Scanner.Tests/TagFacetCopyrightTests.cs
git commit -m "feat(scanner): facet 樹 kind 分流(只收 copyright+character)+ copyright 聚合 count"
```

---

### Task 6: 前端 facet-sidebar 分區收折 + localStorage + rootless 改名 + 年份 tooltip

**Files:**
- Create: `src/Pm.Web/src/app/features/gallery/facet-sidebar/facet-collapse.ts`
- Test: `src/Pm.Web/src/app/features/gallery/facet-sidebar/facet-collapse.spec.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/facet-sidebar/facet-sidebar.ts`
- Modify: `src/Pm.Web/src/app/features/gallery/facet-sidebar/facet-sidebar.html`
- Modify: `src/Pm.Web/src/app/features/gallery/facet-sidebar/facet-sidebar.css`

**Interfaces:**
- Produces:`facet-collapse.ts`:`type FacetSection='dag'|'general'|'meta'`、`loadCollapsed(Storage):Set<FacetSection>`、`saveCollapsed(Storage,ReadonlySet<FacetSection>):void`、`toggleCollapsed(ReadonlySet<FacetSection>,FacetSection):Set<FacetSection>`。

- [ ] **Step 1: 寫純函式失敗測試**

`facet-collapse.spec.ts`:

```typescript
import { loadCollapsed, saveCollapsed, toggleCollapsed, type FacetSection } from './facet-collapse';

class MemStorage {
  private m = new Map<string, string>();
  getItem(k: string) { return this.m.get(k) ?? null; }
  setItem(k: string, v: string) { this.m.set(k, v); }
  removeItem(k: string) { this.m.delete(k); }
  clear() { this.m.clear(); }
  key() { return null; }
  get length() { return this.m.size; }
}

describe('facet-collapse', () => {
  it('save then load round-trips collapsed sections', () => {
    const s = new MemStorage() as unknown as Storage;
    saveCollapsed(s, new Set<FacetSection>(['general', 'meta']));
    expect([...loadCollapsed(s)].sort()).toEqual(['general', 'meta']);
  });

  it('load returns empty set when nothing stored', () => {
    const s = new MemStorage() as unknown as Storage;
    expect(loadCollapsed(s).size).toBe(0);
  });

  it('load ignores malformed / unknown values', () => {
    const s = new MemStorage() as unknown as Storage;
    s.setItem('pm.facet.collapsed', '["general","bogus","123"]');
    expect([...loadCollapsed(s)]).toEqual(['general']);
  });

  it('toggle adds then removes', () => {
    const a = toggleCollapsed(new Set(), 'dag');
    expect(a.has('dag')).toBe(true);
    const b = toggleCollapsed(a, 'dag');
    expect(b.has('dag')).toBe(false);
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd src/Pm.Web ; npx ng test --watch=false --include='**/facet-collapse.spec.ts'`
（若 include glob 不適用,跑全 `ng test --watch=false`;預期此檔因模組不存在而失敗。）
Expected: FAIL —— `facet-collapse` 模組不存在。

- [ ] **Step 3: 寫純函式實作**

`facet-collapse.ts`:

```typescript
// facet 側欄分區收折狀態的 localStorage 持久化(純函式,易測)。
export type FacetSection = 'dag' | 'general' | 'meta';
const KEY = 'pm.facet.collapsed';
const VALID: readonly FacetSection[] = ['dag', 'general', 'meta'];

export function loadCollapsed(storage: Storage): Set<FacetSection> {
  try {
    const raw = storage.getItem(KEY);
    if (!raw) return new Set();
    const arr = JSON.parse(raw) as unknown;
    if (!Array.isArray(arr)) return new Set();
    return new Set(arr.filter((s): s is FacetSection => VALID.includes(s as FacetSection)));
  } catch {
    return new Set();
  }
}

export function saveCollapsed(storage: Storage, set: ReadonlySet<FacetSection>): void {
  try {
    storage.setItem(KEY, JSON.stringify([...set]));
  } catch {
    /* 配額/隱私模式:忽略 */
  }
}

export function toggleCollapsed(set: ReadonlySet<FacetSection>, section: FacetSection): Set<FacetSection> {
  const next = new Set(set);
  if (next.has(section)) next.delete(section);
  else next.add(section);
  return next;
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd src/Pm.Web ; npx ng test --watch=false`
Expected: facet-collapse 4 測 PASS,全測試綠。

- [ ] **Step 5: 接進 component(.ts)**

`facet-sidebar.ts`:頂部 import 加 `import { loadCollapsed, saveCollapsed, toggleCollapsed, type FacetSection } from './facet-collapse';`。在 class 內加:

```typescript
  // 分區整段收折(dag/屬性/年份),狀態存 localStorage,預設全展。
  private readonly collapsed = signal<Set<FacetSection>>(loadCollapsed(localStorage));
  readonly isCollapsed = (s: FacetSection): boolean => this.collapsed().has(s);
  toggleSection(s: FacetSection): void {
    const next = toggleCollapsed(this.collapsed(), s);
    this.collapsed.set(next);
    saveCollapsed(localStorage, next);
  }
```

- [ ] **Step 6: 接進 template(.html)**

`facet-sidebar.html` 三個分區做相同改造(以「屬性」為例;DAG 與年份比照):

把屬性分區的標題列(原 `<div class="facet-t">…屬性</div>`)改成可點 + chevron,並把分區 body 用 `@if` 包住:

```html
  <!-- 屬性 -->
  <div class="facet">
    <div class="facet-t toggleable" role="button" tabindex="0"
         (click)="toggleSection('general')"
         (keydown.enter)="toggleSection('general')" (keydown.space)="$event.preventDefault(); toggleSection('general')">
      <span class="ttoggle" [class.open]="!isCollapsed('general')">▶</span>
      <span class="dot" [style.color]="color('general')" [style.background]="color('general')"></span>
      屬性
    </div>
    @if (!isCollapsed('general')) {
      @for (row of general(); track row[0]) {
        <div class="frow pickable" (click)="pickName(row[0], 'general')">
          <span class="tspacer"></span>
          <span>{{ row[0] }}</span>
          <span class="n">{{ fmt(row[1]) }}</span>
        </div>
      }
    }
  </div>
```

DAG 分區(`'dag'`):同樣在最外層 `<div class="facet">` 的 `<div class="facet-t">…作品 / 企劃 → 角色…</div>` 加 chevron + role/tabindex + `(click)="toggleSection('dag')"`(保留右側 `<span class="dag">DAG</span>`),並把**整個** DAG 內容(depth-0 `@for` 樹 + 「無上層分類」小標 + rootless `@for`)包進 `@if (!isCollapsed('dag')) { … }`。

年份分區(`'meta'`):比照屬性;**標題列加 tooltip** `title="你收錄／存圖的年份,非作品發行年"`。

**rootless 標題改名**:把 `<div class="facet-t rootless-h">— 無上層分類 —</div>` 文字改為 `— 角色(無作品)—`。

- [ ] **Step 7: .css 微調**

`facet-sidebar.css` 加(沿用既有 `.ttoggle` 旋轉模式;分區標題可點游標):

```css
.facet-t.toggleable {
  cursor: pointer;
  user-select: none;
  border-radius: 6px;
}
.facet-t.toggleable:hover {
  background: var(--color-raised);
}
```

（`.ttoggle` 與 `.ttoggle.open` 旋轉已存在;`:focus-visible` ring 由全域提供,勿加 `outline:none`。）

- [ ] **Step 8: build + 手測**

Run: `cd src/Pm.Web ; npx ng build`
Expected: build 0 error。手測:三區標題可點收合/展開,chevron 旋轉;重整後收合狀態保留;rootless 顯示「角色(無作品)」;年份標題 hover 出 tooltip。

- [ ] **Step 9: Commit**

```bash
git add src/Pm.Web/src/app/features/gallery/facet-sidebar/
git commit -m "feat(web): facet 側欄分區整段收折 + localStorage + rootless 改名 + 年份 tooltip"
```

---

### Task 7: 整合測試 —— 搜尋作品命中子角色(closure 串接)

**Files:**
- Test: `tests/Pm.Api.Tests/CopyrightAxisApiTests.cs`(加測試)或既有查詢測試檔。

**Interfaces:**
- 驗證既有查詢端點(搜尋 tag)經 `PhotoQueryService` 的 `closure.DescendantsAsync` 展開,搜 copyright 命中子角色的 photo。**無程式變更**,純驗證。

- [ ] **Step 1: 確認查詢端點與 token 形狀**

開啟 `src/Pm.Api/Program.cs` 找搜尋/查詢端點(布林 tag 查詢,例如 `POST /api/search` 或 `GET /api/photos?...`),與既有 `QueryApiTests.cs` 對照其 request/response 形狀(tag token 怎麼帶、回傳 photo 清單欄位)。以該既有測試為樣板。

- [ ] **Step 2: 寫整合測試**

於 `CopyrightAxisApiTests` 加(請依 Step 1 確認的實際查詢端點/DTO 調整 URL 與 payload):

```csharp
[Fact]
public async Task Search_by_copyright_matches_child_character_photos()
{
    long photoId;
    using (var scope = _factory.Services.CreateScope())
    {
        var ctx = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var tags = scope.ServiceProvider.GetRequiredService<TagService>();
        var axis = scope.ServiceProvider.GetRequiredService<CopyrightAxisService>();
        var root = new Pm.Data.Entities.LibraryRoot { Name = "r", AbsPath = @"C:\x" };
        ctx.LibraryRoots.Add(root); await ctx.SaveChangesAsync();
        var photo = new Pm.Data.Entities.Photo { FileHash = new string('b', 64), ImportedAt = DateTimeOffset.UtcNow };
        ctx.Photos.Add(photo); await ctx.SaveChangesAsync();
        photoId = photo.Id;
        ctx.PhotoLocations.Add(new Pm.Data.Entities.PhotoLocation { PhotoId = photo.Id, LibraryRootId = root.Id, RelPath = "a.png", Status = "present", FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow });
        var character = await tags.UpsertByNameAsync("aris_(blue_archive)", "character");
        ctx.PhotoTags.Add(new Pm.Data.Entities.PhotoTag { PhotoId = photo.Id, TagId = character.Id, Source = "wd14" });
        await ctx.SaveChangesAsync();
        await axis.SeedFromCharacterAsync(character);   // 建 blue_archive + 邊
    }
    var client = _factory.CreateClient();

    // 依實際查詢端點搜 "blue_archive"(copyright 父標),應命中只掛子角色標的 photo。
    // 範例(請依 QueryApiTests 的實際形狀替換):
    var resp = await client.GetAsync("/api/search?all=blue_archive");
    resp.EnsureSuccessStatusCode();
    var json = await resp.Content.ReadAsStringAsync();
    Assert.Contains(photoId.ToString(), json);
}
```

- [ ] **Step 3: 跑測試確認通過**

Run: `dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter "FullyQualifiedName~CopyrightAxisApiTests"`
Expected: PASS —— 證實寫邊後搜 copyright 經 closure 命中子角色 photo(搜尋側零改即生效)。

- [ ] **Step 4: 全測試 + Commit**

Run: `dotnet test`
Expected: 全套綠。

```bash
git add tests/Pm.Api.Tests/CopyrightAxisApiTests.cs
git commit -m "test(api): 搜尋作品經 closure 命中子角色 photo(端到端驗證)"
```

---

## Self-Review

**Spec coverage(對照 `2026-06-25-tag-copyright-axis-design.md`):**
- §3.1 ParseWork(移植前端 + 黑名單) → Task 1。✓
- §3.2 seed copyright tag + 冪等邊 + 防環/自我 → Task 2。✓（source 欄不存在,以 Global Constraints 修正記錄)
- §3.3(a) TaggingWorker 即時 → Task 3;(b) backfill 端點 → Task 4。✓
- §3.4 closure 零改 + 驗證 → Task 7。✓
- §3.5 copyright 聚合 count → Task 5（採 (ii) 近似,spec 容許)。✓
- §3.6 kind 分流(tree/rootless 只 copyright+character) → Task 5。✓
- §3.7 前端收折 + localStorage + rootless 改名 + 年份 tooltip → Task 6。✓
- §四 年份語意(維持標題 + tooltip) → Task 6 Step 6。✓

**Placeholder scan:** 無 TBD;每 code step 有完整程式碼。Task 3 Step 3 與 Task 7 因依賴既有測試/端點形狀,明確指示「開既有檔對照、依實際形狀調整」並給出斷言碼 —— 非待填,是與既有碼整合的必要對照。✓

**Type consistency:** `CopyrightAxis.ParseWork(string)->string?`、`CopyrightAxisService.SeedFromCharacterAsync(Tag,ct)->Task<bool>`、`TagRelation{ParentTagId,ChildTagId}`、facet `{scanned,edgesCreated}`、`FacetSection`/`loadCollapsed/saveCollapsed/toggleCollapsed` 各 Task 一致。✓
