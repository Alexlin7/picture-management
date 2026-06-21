# Phase 1 布林查詢 API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **前置:** 需先完成地基、Scanner(身分/位置、EXIF/縮圖/對帳)、路徑→tag 確認。

**Goal:** 提供前端要的查詢與取圖 API:布林多軸 tag 查詢(AND/排除)+ **tag implication**(搜上層自動涵蓋 DAG 後代,recursive CTE)+ keyset 分頁;串流縮圖與照片明細;Saved Search CRUD。只回有 present 位置的照片。

**Architecture:** `Pm.Scanner`(或共用)加 `TagClosureService`(recursive CTE 求某 tag 的後代閉包)與 `PhotoQueryService`(把每個查詢 tag 展成閉包、要求照片同時命中所有群組、keyset 分頁)。`Pm.Api` 加 `/api/search`、`/api/photos/{id}/thumb`、`/api/photos/{id}`、`/api/saved-searches` CRUD。

**Tech Stack:** .NET 10、EF Core 10.x SQLite(`Database.SqlQuery` recursive CTE)、xUnit。

## Global Constraints

- **布林多軸查詢**:含 N 個 tag 的交集 + 排除;搜上層自動涵蓋 DAG 後代(tag implication)。
- **keyset 分頁**(`id < cursor ORDER BY id DESC`),非 OFFSET。
- **只回有 present 位置的照片**(archived/missing 不出現在一般瀏覽)。
- **縮圖串流**只讀快取檔,不碰原圖。

---

## File Structure

```
src/
├─ Pm.Scanner/
│  ├─ TagClosureService.cs         # recursive CTE 後代閉包
│  ├─ PhotoQueryModels.cs          # PhotoListItem / PhotoPage
│  └─ PhotoQueryService.cs         # 布林 AND 閉包 + 排除 + keyset
└─ Pm.Api/
   └─ Program.cs                   # +search/thumb/detail/saved-search 端點
tests/
├─ Pm.Scanner.Tests/
│  ├─ TagClosureTests.cs
│  └─ PhotoQueryTests.cs
└─ Pm.Api.Tests/
   └─ QueryApiTests.cs
```

---

## Task 1: `TagClosureService`(DAG 後代閉包,recursive CTE)

給一個 tag id,回它自己 + 所有後代(沿 `tag_relation` parent→child)。tag implication 的核心。

**Files:**
- Create: `src/Pm.Scanner/TagClosureService.cs`
- Create: `tests/Pm.Scanner.Tests/TagClosureTests.cs`

**Interfaces:**
- Consumes: `PmDbContext`、`tag_relation`。
- Produces: `TagClosureService(PmDbContext db)`,方法 `Task<List<long>> DescendantsAsync(long tagId, CancellationToken ct = default)`(含自身)。

- [ ] **Step 1: 寫 service**

Create `src/Pm.Scanner/TagClosureService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Scanner;

public sealed class TagClosureService(PmDbContext db)
{
    /// <summary>tag 自身 + 所有後代(DAG,沿 tag_relation parent→child)。應用層保證無環。</summary>
    public async Task<List<long>> DescendantsAsync(long tagId, CancellationToken ct = default) =>
        await db.Database.SqlQuery<long>($@"
            WITH RECURSIVE descendants(id) AS (
                SELECT {tagId}
                UNION
                SELECT tr.child_tag_id
                FROM tag_relation tr
                JOIN descendants d ON tr.parent_tag_id = d.id
            )
            SELECT id AS ""Value"" FROM descendants").ToListAsync(ct);
}
```

- [ ] **Step 2: 寫失敗的測試**

建一個三層 DAG + 一個多父節點,驗證閉包。

Create `tests/Pm.Scanner.Tests/TagClosureTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class TagClosureTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-closure-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public TagClosureTests()
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

    [Fact]
    public async Task Descendants_includes_self_children_and_grandchildren()
    {
        long agency, project, chara;
        await using (var ctx = NewContext())
        {
            var a = new Tag { Name = "2434", Kind = "copyright" };       // 企劃
            var p = new Tag { Name = "vspo", Kind = "copyright" };       // 作品
            var c = new Tag { Name = "tokino_sora", Kind = "character" };// 角色
            ctx.Tags.AddRange(a, p, c);
            await ctx.SaveChangesAsync();
            ctx.TagRelations.Add(new TagRelation { ParentTagId = a.Id, ChildTagId = p.Id });
            ctx.TagRelations.Add(new TagRelation { ParentTagId = p.Id, ChildTagId = c.Id });
            await ctx.SaveChangesAsync();
            agency = a.Id; project = p.Id; chara = c.Id;
        }

        await using var ctx2 = NewContext();
        var closure = new TagClosureService(ctx2);

        var top = await closure.DescendantsAsync(agency);
        Assert.Equal(new[] { agency, project, chara }.OrderBy(x => x), top.OrderBy(x => x));

        var leaf = await closure.DescendantsAsync(chara);
        Assert.Equal(new[] { chara }, leaf);   // 葉只有自己
    }

    [Fact]
    public async Task Multi_parent_node_appears_under_each_parent_closure()
    {
        long p1, p2, shared;
        await using (var ctx = NewContext())
        {
            var a = new Tag { Name = "projA", Kind = "copyright" };
            var b = new Tag { Name = "projB", Kind = "copyright" };
            var s = new Tag { Name = "collab_unit", Kind = "character" };
            ctx.Tags.AddRange(a, b, s);
            await ctx.SaveChangesAsync();
            ctx.TagRelations.Add(new TagRelation { ParentTagId = a.Id, ChildTagId = s.Id });
            ctx.TagRelations.Add(new TagRelation { ParentTagId = b.Id, ChildTagId = s.Id });
            await ctx.SaveChangesAsync();
            p1 = a.Id; p2 = b.Id; shared = s.Id;
        }

        await using var ctx2 = NewContext();
        var closure = new TagClosureService(ctx2);
        Assert.Contains(shared, await closure.DescendantsAsync(p1));
        Assert.Contains(shared, await closure.DescendantsAsync(p2));
    }
}
```

- [ ] **Step 3: 跑測試 + Commit**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter TagClosureTests
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: TagClosureService(DAG 後代閉包 recursive CTE,tag implication 基礎)"
```

Expected: PASS,2 passed。

---

## Task 2: `PhotoQueryService`(布林 AND 閉包 + 排除 + keyset)

照片必須同時命中**每個**查詢 tag 的閉包(含後代),排除命中任一排除閉包者,只回有 present 位置者,keyset 分頁。

**Files:**
- Create: `src/Pm.Scanner/PhotoQueryModels.cs`
- Create: `src/Pm.Scanner/PhotoQueryService.cs`
- Create: `tests/Pm.Scanner.Tests/PhotoQueryTests.cs`

**Interfaces:**
- Consumes: `PmDbContext`、`TagClosureService`。
- Produces:
  - `record PhotoListItem(long Id, string FileHash, int? Width, int? Height, string? Mime)`
  - `record PhotoPage(IReadOnlyList<PhotoListItem> Items, long? NextCursor)`
  - `PhotoQueryService(PmDbContext db, TagClosureService closure)`,方法 `Task<PhotoPage> SearchAsync(IEnumerable<string> all, IEnumerable<string> none, long? afterId, int pageSize, CancellationToken ct = default)`

- [ ] **Step 1: 寫模型與 service**

Create `src/Pm.Scanner/PhotoQueryModels.cs`:

```csharp
namespace Pm.Scanner;

public sealed record PhotoListItem(long Id, string FileHash, int? Width, int? Height, string? Mime);
public sealed record PhotoPage(IReadOnlyList<PhotoListItem> Items, long? NextCursor);
```

Create `src/Pm.Scanner/PhotoQueryService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Scanner;

public sealed class PhotoQueryService(PmDbContext db, TagClosureService closure)
{
    public async Task<PhotoPage> SearchAsync(
        IEnumerable<string> all, IEnumerable<string> none,
        long? afterId, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);

        // 每個 include tag → 後代閉包群組;照片需命中所有群組。
        var includeGroups = new List<List<long>>();
        foreach (var name in all.Distinct())
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is null) return new PhotoPage(Array.Empty<PhotoListItem>(), null);  // 未知 tag → 無結果
            includeGroups.Add(await closure.DescendantsAsync(tag.Id, ct));
        }

        var excludeIds = new List<long>();
        foreach (var name in none.Distinct())
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
            if (tag is not null) excludeIds.AddRange(await closure.DescendantsAsync(tag.Id, ct));
        }

        var q = db.Photos.Where(p => p.Locations.Any(l => l.Status == "present"));
        foreach (var group in includeGroups)
            q = q.Where(p => p.Tags.Any(t => group.Contains(t.TagId)));
        if (excludeIds.Count > 0)
            q = q.Where(p => !p.Tags.Any(t => excludeIds.Contains(t.TagId)));
        if (afterId is not null)
            q = q.Where(p => p.Id < afterId);

        var rows = await q.OrderByDescending(p => p.Id).Take(pageSize + 1)
            .Select(p => new PhotoListItem(p.Id, p.FileHash, p.Width, p.Height, p.Mime))
            .ToListAsync(ct);

        long? next = rows.Count > pageSize ? rows[pageSize - 1].Id : null;
        if (rows.Count > pageSize) rows.RemoveAt(rows.Count - 1);
        return new PhotoPage(rows, next);
    }
}
```

- [ ] **Step 2: 寫失敗的測試**

驗證:AND 交集、tag implication(搜上層命中只標下層的照片)、排除、keyset 分頁、只回 present。

Create `tests/Pm.Scanner.Tests/PhotoQueryTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class PhotoQueryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-query-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public PhotoQueryTests()
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

    private PhotoQueryService Svc(PmDbContext ctx) => new(ctx, new TagClosureService(ctx));

    // 建一張有 present 位置、掛指定 tag 的照片
    private async Task<long> AddPhoto(PmDbContext ctx, LibraryRoot root, string hash, params long[] tagIds)
    {
        var photo = new Photo { FileHash = hash.PadRight(64, '0'), FileSize = 1 };
        photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = hash + ".png", Status = "present" });
        foreach (var tid in tagIds) photo.Tags.Add(new PhotoTag { TagId = tid, Source = "manual" });
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
        return photo.Id;
    }

    [Fact]
    public async Task And_intersection_and_implication_and_exclude()
    {
        long vspo, pekora, nsfw; long root;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "t", AbsPath = @"D:\x" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); root = r.Id;

            var t_vspo = new Tag { Name = "vspo", Kind = "copyright" };
            var t_pekora = new Tag { Name = "pekora", Kind = "character" };
            var t_nsfw = new Tag { Name = "nsfw", Kind = "meta" };
            ctx.Tags.AddRange(t_vspo, t_pekora, t_nsfw); await ctx.SaveChangesAsync();
            ctx.TagRelations.Add(new TagRelation { ParentTagId = t_vspo.Id, ChildTagId = t_pekora.Id });
            await ctx.SaveChangesAsync();
            vspo = t_vspo.Id; pekora = t_pekora.Id; nsfw = t_nsfw.Id;

            await AddPhoto(ctx, r, "p1", pekora);          // 只標子 pekora
            await AddPhoto(ctx, r, "p2", pekora, nsfw);    // pekora + nsfw
            await AddPhoto(ctx, r, "p3");                  // 無 tag
        }

        await using var ctx2 = NewContext();
        var svc = Svc(ctx2);

        // 搜上層 vspo → implication 命中 p1、p2(都只掛子 pekora)
        var byParent = await svc.SearchAsync(["vspo"], [], null, 200);
        Assert.Equal(2, byParent.Items.Count);

        // vspo 但排除 nsfw → 只剩 p1
        var excl = await svc.SearchAsync(["vspo"], ["nsfw"], null, 200);
        Assert.Single(excl.Items);

        // 未知 tag → 無結果
        var unknown = await svc.SearchAsync(["nonexistent"], [], null, 200);
        Assert.Empty(unknown.Items);
    }

    [Fact]
    public async Task Keyset_pagination_walks_all_pages()
    {
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "t", AbsPath = @"D:\x" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync();
            for (int i = 0; i < 5; i++) await AddPhoto(ctx, r, $"k{i}");
        }

        await using var ctx2 = NewContext();
        var svc = Svc(ctx2);

        var page1 = await svc.SearchAsync([], [], null, 2);
        Assert.Equal(2, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);

        var page2 = await svc.SearchAsync([], [], page1.NextCursor, 2);
        Assert.Equal(2, page2.Items.Count);

        var page3 = await svc.SearchAsync([], [], page2.NextCursor, 2);
        Assert.Single(page3.Items);
        Assert.Null(page3.NextCursor);   // 最後一頁
    }

    [Fact]
    public async Task Only_present_photos_returned()
    {
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "t", AbsPath = @"D:\x" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync();
            var photo = new Photo { FileHash = new string('m', 64), FileSize = 1 };
            photo.Locations.Add(new PhotoLocation { LibraryRoot = r, RelPath = "gone.png", Status = "missing" });
            ctx.Photos.Add(photo); await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        Assert.Empty((await Svc(ctx2).SearchAsync([], [], null, 200)).Items);
    }
}
```

- [ ] **Step 3: 跑測試 + Commit**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter PhotoQueryTests
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: PhotoQueryService(布林 AND 閉包 + 排除 + keyset,只回 present)"
```

Expected: PASS,3 passed。

---

## Task 3: 查詢 / 縮圖 / 明細端點

**Files:**
- Modify: `src/Pm.Api/Program.cs`
- Create: `tests/Pm.Api.Tests/QueryApiTests.cs`

**Interfaces:**
- Produces:
  - `POST /api/search`,body `{ all?: string[], none?: string[], afterId?: long, pageSize?: int }` → `200` `PhotoPage`
  - `GET /api/photos/{id:long}/thumb` → webp(`200`)或 `404`
  - `GET /api/photos/{id:long}` → `200` 明細(含 locations、tags(name/kind/source/confidence))或 `404`

- [ ] **Step 1: 註冊 service + 端點**

在 `src/Pm.Api/Program.cs` 服務註冊區加:

```csharp
builder.Services.AddScoped<TagClosureService>();
builder.Services.AddScoped<PhotoQueryService>();
```

端點區加:

```csharp
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
```

DTO(檔末加):

```csharp
public record SearchDto(string[]? All, string[]? None, long? AfterId, int? PageSize);
```

- [ ] **Step 2: 寫失敗的端點測試**

Create `tests/Pm.Api.Tests/QueryApiTests.cs`:

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

public class QueryApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-qapi-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-qroot-{Guid.NewGuid():N}");
    private readonly string _thumbs = Path.Combine(Path.GetTempPath(), $"pm-qthumbs-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public QueryApiTests()
    {
        Directory.CreateDirectory(_root);
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
    private record Item(long Id, string FileHash);
    private record Page(List<Item> Items, long? NextCursor);

    [Fact]
    public async Task Scan_then_search_thumb_and_detail()
    {
        using (var img = new Image<Rgba32>(40, 30)) await img.SaveAsPngAsync(Path.Combine(_root, "a.png"));

        var client = _factory.CreateClient();
        var root = await (await client.PostAsJsonAsync("/api/roots", new { name = "t", absPath = _root }))
            .Content.ReadFromJsonAsync<RootCreated>();
        await client.PostAsync($"/api/roots/{root!.Id}/scan", null);

        // 無條件瀏覽應回 1 張
        var page = await (await client.PostAsJsonAsync("/api/search", new { }))
            .Content.ReadFromJsonAsync<Page>();
        Assert.Single(page!.Items);
        var id = page.Items[0].Id;

        // 縮圖串流
        var thumb = await client.GetAsync($"/api/photos/{id}/thumb");
        Assert.Equal(HttpStatusCode.OK, thumb.StatusCode);
        Assert.Equal("image/webp", thumb.Content.Headers.ContentType!.MediaType);

        // 明細
        var detail = await client.GetAsync($"/api/photos/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.Content.ReadAsStringAsync();
        Assert.Contains("\"width\":40", body);

        // 不存在 → 404
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/photos/99999")).StatusCode);
    }
}
```

- [ ] **Step 3: 跑測試 + Commit**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter QueryApiTests
git add src/Pm.Api tests/Pm.Api.Tests
git commit -m "feat: 查詢/縮圖/明細端點(/api/search、/photos/{id}/thumb、/photos/{id})"
```

Expected: PASS,1 passed。

---

## Task 4: Saved Search CRUD

存查詢不存資料夾:Saved Search 一級物件。

**Files:**
- Modify: `src/Pm.Api/Program.cs`
- Create: `tests/Pm.Api.Tests/SavedSearchApiTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/saved-searches` → `200` 陣列
  - `POST /api/saved-searches`,body `{ name: string, queryJson: string }` → `201` `{ id }`
  - `DELETE /api/saved-searches/{id:long}` → `204` / `404`

- [ ] **Step 1: 加端點 + DTO**

在 `src/Pm.Api/Program.cs` 端點區加:

```csharp
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
```

DTO(檔末加):

```csharp
public record SavedSearchDto(string Name, string QueryJson);
```

- [ ] **Step 2: 寫失敗的測試**

Create `tests/Pm.Api.Tests/SavedSearchApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Pm.Api.Tests;

public class SavedSearchApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-ssapi-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public SavedSearchApiTests()
    {
        var db = _dbPath;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={db};Foreign Keys=True"
                })));
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private record Created(long Id);

    [Fact]
    public async Task Create_list_delete_saved_search()
    {
        var client = _factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/saved-searches",
            new { name = "可能是個人照片", queryJson = "{\"all\":[],\"hasExif\":true}" }))
            .Content.ReadFromJsonAsync<Created>();
        Assert.NotNull(created);

        var list = await client.GetStringAsync("/api/saved-searches");
        Assert.Contains("可能是個人照片", list);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/saved-searches/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/saved-searches/{created.Id}")).StatusCode);
    }
}
```

- [ ] **Step 3: 全 solution 驗收 + Commit**

Run:

```bash
cd /d/picture-management
dotnet test
git add src/Pm.Api tests/Pm.Api.Tests
git commit -m "feat: Saved Search CRUD 端點"
```

Expected: 全綠(累計約 **48 passed**)。

---

## 完成定義(布林查詢 API)

- `POST /api/search`:布林 AND + 排除 + tag implication(搜上層涵蓋 DAG 後代)+ keyset 分頁,只回有 present 位置者。
- `GET /api/photos/{id}/thumb`:串流快取 webp,缺檔 404。
- `GET /api/photos/{id}`:明細含 locations 與分 kind/source 的 tags。
- Saved Search CRUD。
- `dotnet test` 全綠。

**明確不在本計畫:** 前端(計畫 6);WD14 寫 tag(計畫 7);語意/向量查詢(Phase 2)。

---

## Self-Review 註記

- **Spec 覆蓋:** §4.4 布林交集、§4.2b tag implication(recursive CTE)、§5.3 瀏覽/keyset/取縮圖、§6 檢視器明細(分 kind/source)、saved_search 一級物件。
- **型別一致:** `PhotoListItem`/`PhotoPage`、`SearchAsync` 簽章、端點 DTO 一致;`thumbs.PathFor` 沿用計畫 3。
```
