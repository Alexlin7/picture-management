# 資料夾瀏覽維度 — 後端實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 後端提供「即時資料夾樹 + 範圍(資料夾)內疊 tag 查詢 + 夾內可用 tag 聚合」三組能力,讓前端做資料夾瀏覽維度。

**Architecture:** 新增 `FolderTreeService`(讀 `photo_location.rel_path` 在記憶體建樹、後序算遞迴 distinct photo 數);擴充既有 `PhotoQueryService.SearchAsync/CountAsync` 多收 `rootId`/`pathPrefix` 把查詢限縮到某資料夾子樹;全部複用既有 EF Core + SQLite,不改 schema、不加 migration。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、EF Core 10 + SQLite、xUnit 2.9。

## Global Constraints

- 不改 schema、不加 migration:資料夾樹一律由現有 `photo_location.rel_path`(正斜線正規化,≤1024)即時推導。
- 只算 `Status == "present"` 的 location(對齊既有 facet/search 語意);計數一律 **distinct photo**(一張多 location 去重)。
- 路徑前綴比對用 `RelPath.StartsWith(prefix + "/")`,根層 `prefix == ""` 代表整 root;避免 `Pixiv` 誤中 `Pixiv2`。
- 既有 `SearchDto`/`SearchAsync`/`CountAsync` 的呼叫端不可破壞:新參數一律加在尾端且可為 null。
- 測試慣例:每測試獨立 temp SQLite 檔(`Path.GetTempPath()` + GUID),連線字串帶 `Foreign Keys=True`;ctor `Database.Migrate()`;`Dispose` 呼叫 `SqliteConnection.ClearAllPools()` 後刪檔;xUnit `Assert.*`(無 FluentAssertions)。
- 服務以 `builder.Services.AddScoped<T>()` 註冊;端點用 Minimal API `app.MapGet/MapPost(...).WithTags(...)`。
- 全程繁體中文(台灣)註解;識別子保留原文。

---

### Task 1: FolderTreeService — 即時資料夾樹 + root 摘要 + 端點

**Files:**
- Create: `src/Pm.Scanner/FolderTreeService.cs`
- Modify: `src/Pm.Api/Program.cs`(服務註冊區 + 端點區)
- Test: `tests/Pm.Scanner.Tests/FolderTreeTests.cs`
- Test: `tests/Pm.Api.Tests/FolderBrowseApiTests.cs`

**Interfaces:**
- Produces:
  - `record FolderNode(string Name, string RelPath, int PhotoCount, List<FolderNode>? Children)`
  - `record FolderRoot(long Id, string Name, int PhotoCount)`
  - `FolderTreeService(PmDbContext db)` 含
    - `Task<List<FolderRoot>> BuildRootsAsync(CancellationToken ct = default)`
    - `Task<FolderNode?> BuildTreeAsync(long rootId, CancellationToken ct = default)`(root 不存在回 `null`)
  - 端點 `GET /api/folder-roots`、`GET /api/roots/{id:long}/folder-tree`

- [ ] **Step 1: 寫 service 失敗測試**

Create `tests/Pm.Scanner.Tests/FolderTreeTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class FolderTreeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-folder-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public FolderTreeTests()
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

    // 建一張在指定 root、指定相對路徑、present 的照片
    private async Task AddPhoto(PmDbContext ctx, LibraryRoot root, string hash, string relPath, string status = "present")
    {
        var photo = new Photo { FileHash = hash.PadRight(64, '0'), FileSize = 1 };
        photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = relPath, Status = status });
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task BuildTree_nests_folders_and_counts_recursively_distinct()
    {
        long rootId;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "圖庫", AbsPath = @"D:\圖庫" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); rootId = r.Id;

            await AddPhoto(ctx, r, "a", "Pixiv/2023/a.png");
            await AddPhoto(ctx, r, "b", "Pixiv/2024/b.png");
            await AddPhoto(ctx, r, "c", "Pixiv/2024/c.png");
            await AddPhoto(ctx, r, "d", "top.png");              // 直接放 root 底下
            await AddPhoto(ctx, r, "e", "Pixiv/2024/gone.png", status: "archived"); // 不算
        }

        await using var ctx2 = NewContext();
        var tree = await new FolderTreeService(ctx2).BuildTreeAsync(rootId);

        Assert.NotNull(tree);
        Assert.Equal("圖庫", tree!.Name);
        Assert.Equal("", tree.RelPath);
        Assert.Equal(4, tree.PhotoCount);                        // a,b,c,d(archived e 不算)

        var pixiv = tree.Children!.Single(c => c.Name == "Pixiv");
        Assert.Equal("Pixiv", pixiv.RelPath);
        Assert.Equal(3, pixiv.PhotoCount);                       // a,b,c

        var y2024 = pixiv.Children!.Single(c => c.Name == "2024");
        Assert.Equal("Pixiv/2024", y2024.RelPath);
        Assert.Equal(2, y2024.PhotoCount);                       // b,c(archived 不算)
        Assert.Null(y2024.Children);                             // 葉:無子資料夾
    }

    [Fact]
    public async Task BuildTree_same_named_subfolder_under_different_parents_stays_separate()
    {
        long rootId;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "r", AbsPath = @"D:\r" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); rootId = r.Id;

            await AddPhoto(ctx, r, "a", "Pixiv/蔚藍檔案/a.png");
            await AddPhoto(ctx, r, "b", "Twitter/蔚藍檔案/b.png");
        }

        await using var ctx2 = NewContext();
        var tree = await new FolderTreeService(ctx2).BuildTreeAsync(rootId);

        var pixivBa = tree!.Children!.Single(c => c.Name == "Pixiv").Children!.Single(c => c.Name == "蔚藍檔案");
        var twitterBa = tree.Children!.Single(c => c.Name == "Twitter").Children!.Single(c => c.Name == "蔚藍檔案");
        Assert.Equal("Pixiv/蔚藍檔案", pixivBa.RelPath);
        Assert.Equal("Twitter/蔚藍檔案", twitterBa.RelPath);     // 各自獨立節點,未合併
        Assert.Equal(1, pixivBa.PhotoCount);
        Assert.Equal(1, twitterBa.PhotoCount);
    }

    [Fact]
    public async Task BuildTree_returns_null_for_unknown_root()
    {
        await using var ctx = NewContext();
        Assert.Null(await new FolderTreeService(ctx).BuildTreeAsync(99999));
    }

    [Fact]
    public async Task BuildRoots_lists_each_root_with_distinct_present_count()
    {
        await using (var ctx = NewContext())
        {
            var r1 = new LibraryRoot { Name = "A", AbsPath = @"D:\a" };
            var r2 = new LibraryRoot { Name = "B", AbsPath = @"D:\b" };
            ctx.LibraryRoots.AddRange(r1, r2); await ctx.SaveChangesAsync();
            await AddPhoto(ctx, r1, "a", "x/a.png");
            await AddPhoto(ctx, r1, "b", "x/b.png");
            await AddPhoto(ctx, r2, "c", "y/c.png");
        }

        await using var ctx2 = NewContext();
        var roots = await new FolderTreeService(ctx2).BuildRootsAsync();

        Assert.Equal(2, roots.Count(r => r.PhotoCount > 0));
        Assert.Equal(2, roots.Single(r => r.Name == "A").PhotoCount);
        Assert.Equal(1, roots.Single(r => r.Name == "B").PhotoCount);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Scanner.Tests --filter FolderTreeTests`
Expected: 編譯失敗 — `FolderTreeService` / `FolderNode` / `FolderRoot` 不存在。

- [ ] **Step 3: 寫 FolderTreeService**

Create `src/Pm.Scanner/FolderTreeService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Scanner;

/// <summary>資料夾樹節點:名稱、累積相對路徑前綴(root 為 "")、遞迴 distinct present photo 數、子資料夾。</summary>
public sealed record FolderNode(string Name, string RelPath, int PhotoCount, List<FolderNode>? Children);

/// <summary>root 摘要:供 /browse 頂層並列各來源。</summary>
public sealed record FolderRoot(long Id, string Name, int PhotoCount);

/// <summary>
/// 即時資料夾樹:讀 photo_location.rel_path(只取 present)在記憶體建樹,後序算遞迴 distinct photo 數。
/// 不落表、不改 schema;反映硬碟當下結構,與 path→tag 維度正交。
/// </summary>
public sealed class FolderTreeService(PmDbContext db)
{
    public async Task<List<FolderRoot>> BuildRootsAsync(CancellationToken ct = default)
    {
        var roots = await db.LibraryRoots.Select(r => new { r.Id, r.Name }).ToListAsync(ct);
        var result = new List<FolderRoot>();
        foreach (var r in roots)
        {
            var count = await db.PhotoLocations
                .Where(l => l.LibraryRootId == r.Id && l.Status == "present")
                .Select(l => l.PhotoId).Distinct().CountAsync(ct);
            result.Add(new FolderRoot(r.Id, r.Name, count));
        }
        return result;
    }

    public async Task<FolderNode?> BuildTreeAsync(long rootId, CancellationToken ct = default)
    {
        var rootName = await db.LibraryRoots.Where(r => r.Id == rootId)
            .Select(r => r.Name).FirstOrDefaultAsync(ct);
        if (rootName is null) return null;

        var locs = await db.PhotoLocations
            .Where(l => l.LibraryRootId == rootId && l.Status == "present")
            .Select(l => new { l.RelPath, l.PhotoId })
            .ToListAsync(ct);

        var root = new MutableNode("", "");
        foreach (var loc in locs)
        {
            var parts = loc.RelPath.Split('/');
            var node = root;
            var prefix = "";
            for (var i = 0; i < parts.Length - 1; i++)   // 最後一段是檔名,跳過
            {
                var seg = parts[i];
                if (seg.Length == 0) continue;
                prefix = prefix.Length == 0 ? seg : prefix + "/" + seg;
                node = node.Child(seg, prefix);
            }
            node.PhotoIds.Add(loc.PhotoId);              // 掛到所在資料夾(可能就是 root)
        }

        return root.Fold(rootName).node;
    }

    /// <summary>建樹用的可變節點;Fold 後序合併子樹 photo id 取 distinct count。</summary>
    private sealed class MutableNode(string name, string relPath)
    {
        public string Name { get; } = name;
        public string RelPath { get; } = relPath;
        public Dictionary<string, MutableNode> Kids { get; } = new();
        public HashSet<long> PhotoIds { get; } = new();

        public MutableNode Child(string seg, string prefix)
        {
            if (!Kids.TryGetValue(seg, out var c))
            {
                c = new MutableNode(seg, prefix);
                Kids[seg] = c;
            }
            return c;
        }

        public (FolderNode node, HashSet<long> ids) Fold(string? displayName = null)
        {
            var all = new HashSet<long>(PhotoIds);
            List<FolderNode>? children = null;
            if (Kids.Count > 0)
            {
                children = new List<FolderNode>();
                foreach (var k in Kids.Values.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var (cn, cids) = k.Fold();
                    children.Add(cn);
                    all.UnionWith(cids);
                }
            }
            return (new FolderNode(displayName ?? Name, RelPath, all.Count, children), all);
        }
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Pm.Scanner.Tests --filter FolderTreeTests`
Expected: PASS(4 個測試)。

- [ ] **Step 5: 註冊服務 + 加端點**

Modify `src/Pm.Api/Program.cs` 服務註冊區(在 `builder.Services.AddScoped<TagFacetService>();` 之後加一行):

```csharp
builder.Services.AddScoped<FolderTreeService>();
```

Modify `src/Pm.Api/Program.cs` 端點區(在 `/api/tags/tree` 端點之後加):

```csharp
// 資料夾瀏覽維度:所有 root 摘要(頂層並列)
app.MapGet("/api/folder-roots", async (FolderTreeService svc) =>
    Results.Ok(await svc.BuildRootsAsync()))
    .WithTags("Browse");

// 某 root 的即時資料夾樹(遞迴 distinct present photo 計數)
app.MapGet("/api/roots/{id:long}/folder-tree", async (long id, FolderTreeService svc) =>
    await svc.BuildTreeAsync(id) is { } tree ? Results.Ok(tree) : Results.NotFound())
    .WithTags("Browse");
```

- [ ] **Step 6: 寫端點 API 測試**

Create `tests/Pm.Api.Tests/FolderBrowseApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Api.Tests;

public class FolderBrowseApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-browseapi-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public FolderBrowseApiTests()
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

    private record RootDto(long Id, string Name, int PhotoCount);
    private record NodeDto(string Name, string RelPath, int PhotoCount, List<NodeDto>? Children);

    private long Seed()
    {
        _ = _factory.CreateClient();   // 觸發 Migrate
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var root = new LibraryRoot { Name = "圖庫", AbsPath = @"D:\圖庫" };
        db.LibraryRoots.Add(root); db.SaveChanges();

        void Add(string hash, string rel)
        {
            var p = new Photo { FileHash = hash.PadRight(64, '0'), FileSize = 1 };
            p.Locations.Add(new PhotoLocation { LibraryRootId = root.Id, RelPath = rel, Status = "present" });
            db.Photos.Add(p);
        }
        Add("a", "Pixiv/2024/a.png");
        Add("b", "Pixiv/2024/b.png");
        db.SaveChanges();
        return root.Id;
    }

    [Fact]
    public async Task Folder_roots_and_tree_endpoints_return_expected_shape()
    {
        var rootId = Seed();
        var client = _factory.CreateClient();

        var roots = await client.GetFromJsonAsync<List<RootDto>>("/api/folder-roots");
        Assert.Single(roots!);
        Assert.Equal(2, roots![0].PhotoCount);

        var tree = await client.GetFromJsonAsync<NodeDto>($"/api/roots/{rootId}/folder-tree");
        Assert.Equal("圖庫", tree!.Name);
        Assert.Equal(2, tree.PhotoCount);
        var pixiv = tree.Children!.Single(c => c.Name == "Pixiv");
        Assert.Equal("Pixiv/2024", pixiv.Children!.Single().RelPath);

        var missing = await client.GetAsync("/api/roots/99999/folder-tree");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
```

- [ ] **Step 7: 跑全測試確認通過**

Run: `dotnet test tests/Pm.Api.Tests --filter FolderBrowseApiTests`
Expected: PASS。

- [ ] **Step 8: Commit**

```bash
git add src/Pm.Scanner/FolderTreeService.cs src/Pm.Api/Program.cs tests/Pm.Scanner.Tests/FolderTreeTests.cs tests/Pm.Api.Tests/FolderBrowseApiTests.cs
git commit -m "feat(api): 即時資料夾樹服務 + folder-roots/folder-tree 端點

讀 photo_location.rel_path 在記憶體建樹,後序算遞迴 distinct present
photo 計數;不改 schema。多 root 經 /api/folder-roots 頂層並列。

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01GJJk3bb2x32Ru1AoxhVDZN"
```

---

### Task 2: 範圍查詢(資料夾 AND tag)+ 夾內可用 tag 聚合

**Files:**
- Modify: `src/Pm.Scanner/PhotoQueryService.cs`(SearchAsync/CountAsync 加 `rootId`/`pathPrefix`)
- Modify: `src/Pm.Scanner/FolderTreeService.cs`(加 `FolderTagsAsync` + `FolderTag` record)
- Modify: `src/Pm.Api/Program.cs`(`SearchDto` 加欄位、search 端點傳參、新增 folder-tags 端點)
- Test: `tests/Pm.Scanner.Tests/FolderScopeQueryTests.cs`
- Test: `tests/Pm.Api.Tests/FolderBrowseApiTests.cs`(追加一個測試)

**Interfaces:**
- Consumes(Task 1):`FolderTreeService(PmDbContext db)`、`FolderNode`。
- Produces:
  - `PhotoQueryService.SearchAsync(all, none, afterId, pageSize, rootId, pathPrefix, ct)` — 尾端兩參數 `long? rootId = null, string? pathPrefix = null`。
  - `PhotoQueryService.CountAsync(all, none, rootId, pathPrefix, ct)` — 同上尾端兩參數。
  - `FolderTreeService.FolderTagsAsync(long rootId, string? pathPrefix, CancellationToken ct = default) -> Task<List<FolderTag>>`,`record FolderTag(string Name, string Kind, int Count)`。
  - `SearchDto` 末加 `long? RootId, string? PathPrefix`。
  - 端點 `GET /api/browse/folder-tags?rootId=&path=`。

- [ ] **Step 1: 寫範圍查詢失敗測試**

Create `tests/Pm.Scanner.Tests/FolderScopeQueryTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class FolderScopeQueryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-scope-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public FolderScopeQueryTests()
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

    private PhotoQueryService Query(PmDbContext ctx) => new(ctx, new TagClosureService(ctx));

    private async Task AddPhoto(PmDbContext ctx, LibraryRoot root, string hash, string relPath, params long[] tagIds)
    {
        var photo = new Photo { FileHash = hash.PadRight(64, '0'), FileSize = 1 };
        photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = relPath, Status = "present" });
        foreach (var tid in tagIds) photo.Tags.Add(new PhotoTag { TagId = tid, Source = "manual" });
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task PathPrefix_scopes_recursively_and_avoids_sibling_prefix_collision()
    {
        long rootId;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "r", AbsPath = @"D:\r" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); rootId = r.Id;

            await AddPhoto(ctx, r, "a", "Pixiv/2024/a.png");
            await AddPhoto(ctx, r, "b", "Pixiv/2024/sub/b.png");   // 遞迴應含
            await AddPhoto(ctx, r, "c", "Pixiv2/c.png");           // 不可被 "Pixiv" 前綴誤中
            await AddPhoto(ctx, r, "d", "Twitter/d.png");
        }

        await using var ctx2 = NewContext();
        var svc = Query(ctx2);

        Assert.Equal(2, await svc.CountAsync([], [], rootId, "Pixiv"));        // a,b(遞迴);非 c
        Assert.Equal(2, await svc.CountAsync([], [], rootId, "Pixiv/2024"));   // a,b
        Assert.Equal(4, await svc.CountAsync([], [], rootId, ""));             // 整 root
        Assert.Equal(4, await svc.CountAsync([], [], rootId, null));           // null = 整 root
    }

    [Fact]
    public async Task PathPrefix_combines_with_tag_as_and()
    {
        long rootId, smile;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "r", AbsPath = @"D:\r" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); rootId = r.Id;
            var t = new Tag { Name = "smile", Kind = "general" };
            ctx.Tags.Add(t); await ctx.SaveChangesAsync(); smile = t.Id;

            await AddPhoto(ctx, r, "a", "Pixiv/2024/a.png", smile);   // 夾內 + 有 tag
            await AddPhoto(ctx, r, "b", "Pixiv/2024/b.png");          // 夾內 + 無 tag
            await AddPhoto(ctx, r, "c", "Twitter/c.png", smile);      // 有 tag + 夾外
        }

        await using var ctx2 = NewContext();
        var svc = Query(ctx2);

        Assert.Equal(1, await svc.CountAsync(["smile"], [], rootId, "Pixiv/2024"));  // 只剩 a
        var page = await svc.SearchAsync(["smile"], [], null, 200, rootId, "Pixiv/2024");
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task FolderTags_lists_only_tags_present_in_scope_with_counts()
    {
        long rootId, smile, dress;
        await using (var ctx = NewContext())
        {
            var r = new LibraryRoot { Name = "r", AbsPath = @"D:\r" };
            ctx.LibraryRoots.Add(r); await ctx.SaveChangesAsync(); rootId = r.Id;
            var s = new Tag { Name = "smile", Kind = "general" };
            var d = new Tag { Name = "dress", Kind = "general" };
            ctx.Tags.AddRange(s, d); await ctx.SaveChangesAsync(); smile = s.Id; dress = d.Id;

            await AddPhoto(ctx, r, "a", "Pixiv/2024/a.png", smile, dress);
            await AddPhoto(ctx, r, "b", "Pixiv/2024/b.png", smile);
            await AddPhoto(ctx, r, "c", "Twitter/c.png", dress);   // 夾外的 dress 不該灌進 Pixiv/2024 計數
        }

        await using var ctx2 = NewContext();
        var tags = await new FolderTreeService(ctx2).FolderTagsAsync(rootId, "Pixiv/2024");

        Assert.Equal(2, tags.Count);
        Assert.Equal("smile", tags[0].Name);    // count desc:smile=2 在前
        Assert.Equal(2, tags[0].Count);
        Assert.Equal("dress", tags[1].Name);    // dress=1(只算夾內 a)
        Assert.Equal(1, tags[1].Count);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Scanner.Tests --filter FolderScopeQueryTests`
Expected: 編譯失敗 — `SearchAsync`/`CountAsync` 無 `rootId`/`pathPrefix` 多載;`FolderTagsAsync` 不存在。

- [ ] **Step 3: 給 PhotoQueryService 加 rootId/pathPrefix**

Modify `src/Pm.Scanner/PhotoQueryService.cs`。`SearchAsync` 簽名改為:

```csharp
    public async Task<PhotoPage> SearchAsync(
        IEnumerable<string> all, IEnumerable<string> none,
        long? afterId, int pageSize,
        long? rootId = null, string? pathPrefix = null,
        CancellationToken ct = default)
```

在 `SearchAsync` 內、`var q = db.Photos.Where(p => p.Locations.Any(l => l.Status == "present"));` 這行**之後**插入範圍過濾:

```csharp
        if (rootId is not null)
        {
            var prefix = pathPrefix ?? "";
            q = q.Where(p => p.Locations.Any(l =>
                l.Status == "present"
                && l.LibraryRootId == rootId
                && (prefix == "" || l.RelPath.StartsWith(prefix + "/"))));
        }
```

`CountAsync` 簽名改為:

```csharp
    public async Task<long> CountAsync(
        IEnumerable<string> all, IEnumerable<string> none,
        long? rootId = null, string? pathPrefix = null,
        CancellationToken ct = default)
```

在 `CountAsync` 內同一位置(base `var q = ...present...` 之後)插入**相同**的範圍過濾區塊(同上 6 行)。

- [ ] **Step 4: 給 FolderTreeService 加 FolderTagsAsync**

Modify `src/Pm.Scanner/FolderTreeService.cs`。在 `FolderRoot` record 下方加:

```csharp
/// <summary>夾內可用 tag:該路徑前綴範圍內 distinct present photo 的 tag 聚合(count desc)。</summary>
public sealed record FolderTag(string Name, string Kind, int Count);
```

在 `FolderTreeService` class 內加方法:

```csharp
    public async Task<List<FolderTag>> FolderTagsAsync(long rootId, string? pathPrefix, CancellationToken ct = default)
    {
        var prefix = pathPrefix ?? "";

        var photoIds = db.PhotoLocations
            .Where(l => l.LibraryRootId == rootId && l.Status == "present"
                && (prefix == "" || l.RelPath.StartsWith(prefix + "/")))
            .Select(l => l.PhotoId)
            .Distinct();

        var counts = await db.PhotoTags
            .Where(pt => photoIds.Contains(pt.PhotoId))
            .GroupBy(pt => pt.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Select(x => x.PhotoId).Distinct().Count() })
            .ToListAsync(ct);

        var meta = (await db.Tags.Select(t => new { t.Id, t.Name, t.Kind }).ToListAsync(ct))
            .ToDictionary(t => t.Id);

        return counts
            .Where(c => meta.ContainsKey(c.TagId))
            .Select(c => new FolderTag(meta[c.TagId].Name, meta[c.TagId].Kind, c.Count))
            .OrderByDescending(t => t.Count).ThenBy(t => t.Name)
            .ToList();
    }
```

- [ ] **Step 5: 跑 service 測試確認通過**

Run: `dotnet test tests/Pm.Scanner.Tests --filter FolderScopeQueryTests`
Expected: PASS(3 個測試)。

- [ ] **Step 6: SearchDto 加欄位 + 端點傳參 + folder-tags 端點**

Modify `src/Pm.Api/Program.cs`。`SearchDto` 改為:

```csharp
/// <summary>布林查詢請求:all 取交集、none 排除、keyset 分頁(AfterId + PageSize);RootId+PathPrefix 限縮到某資料夾子樹(瀏覽維度)。</summary>
public record SearchDto(string[]? All, string[]? None, long? AfterId, int? PageSize, long? RootId, string? PathPrefix);
```

`/api/search` 與 `/api/search/count` 端點改為傳入新參:

```csharp
app.MapPost("/api/search", async (SearchDto dto, PhotoQueryService svc) =>
    Results.Ok(await svc.SearchAsync(dto.All ?? [], dto.None ?? [], dto.AfterId, dto.PageSize ?? 200, dto.RootId, dto.PathPrefix)))
    .WithTags("Search");

app.MapPost("/api/search/count", async (SearchDto dto, PhotoQueryService svc) =>
    Results.Ok(new { total = await svc.CountAsync(dto.All ?? [], dto.None ?? [], dto.RootId, dto.PathPrefix) }))
    .WithTags("Search");
```

在 `/api/roots/{id:long}/folder-tree` 端點之後加:

```csharp
// 夾內可用 tag(自動完成用):範圍內 distinct present photo 的 tag 聚合
app.MapGet("/api/browse/folder-tags", async (long rootId, string? path, FolderTreeService svc) =>
    Results.Ok(await svc.FolderTagsAsync(rootId, path)))
    .WithTags("Browse");
```

- [ ] **Step 7: 追加 API 測試**

Modify `tests/Pm.Api.Tests/FolderBrowseApiTests.cs`,在 class 內加(沿用既有 `Seed()`):

```csharp
    private record FolderTagDto(string Name, string Kind, int Count);

    [Fact]
    public async Task Folder_tags_and_scoped_search_endpoints_work()
    {
        var rootId = Seed();   // 兩張圖都在 Pixiv/2024,無 tag
        var client = _factory.CreateClient();

        // 夾內查詢:Pixiv/2024 含 2 張
        var page = await (await client.PostAsJsonAsync("/api/search",
            new { rootId, pathPrefix = "Pixiv/2024" }))
            .Content.ReadFromJsonAsync<Page>();
        Assert.Equal(2, page!.Items.Count);

        // 夾外前綴:Twitter 無圖
        var empty = await (await client.PostAsJsonAsync("/api/search/count",
            new { rootId, pathPrefix = "Twitter" }))
            .Content.ReadAsStringAsync();
        Assert.Contains("\"total\":0", empty);

        // folder-tags 端點可呼叫(seed 無 tag → 空陣列)
        var tags = await client.GetFromJsonAsync<List<FolderTagDto>>(
            $"/api/browse/folder-tags?rootId={rootId}&path=Pixiv/2024");
        Assert.NotNull(tags);
        Assert.Empty(tags!);
    }

    private record Page(List<PageItem> Items, long? NextCursor);
    private record PageItem(long Id, string FileHash);
```

- [ ] **Step 8: 跑全後端測試確認通過**

Run: `dotnet test tests/Pm.Api.Tests --filter FolderBrowseApiTests` 然後 `dotnet test`
Expected: 全 PASS(含既有測試,確認 SearchDto 加欄位未破壞既有 search/count 呼叫)。

- [ ] **Step 9: Commit**

```bash
git add src/Pm.Scanner/PhotoQueryService.cs src/Pm.Scanner/FolderTreeService.cs src/Pm.Api/Program.cs tests/Pm.Scanner.Tests/FolderScopeQueryTests.cs tests/Pm.Api.Tests/FolderBrowseApiTests.cs
git commit -m "feat(api): 搜尋加 rootId/pathPrefix 資料夾範圍 + 夾內可用 tag 端點

SearchAsync/CountAsync 尾端加可選 rootId/pathPrefix(StartsWith prefix+'/'
遞迴、避免兄弟前綴誤中);FolderTagsAsync 聚合範圍內 tag 供自動完成。
SearchDto 新欄位置尾、可為 null,不破壞既有呼叫。

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01GJJk3bb2x32Ru1AoxhVDZN"
```

---

## 後端完成驗證

- [ ] `dotnet build` 綠、`dotnet test`(全測試)綠。
- [ ] 三組能力到位:`GET /api/folder-roots`、`GET /api/roots/{id}/folder-tree`、`POST /api/search(+rootId,pathPrefix)`、`GET /api/browse/folder-tags`。
- [ ] 既有 search/count 行為未變(未帶 rootId 時與原本一致)。

前端 plan(`/browse` 入口、資料夾樹側欄、麵包屑、子夾下鑽、夾內疊 tag UI)待後端綠燈、API 形狀確定後另寫,對接以上端點。

## Self-Review(對 spec)

- spec §四 4.1 folder-tree → Task 1 ✅;多 root(§七)→ `BuildRootsAsync`/`/api/folder-roots` ✅。
- spec §四 4.2 search + pathPrefix → Task 2 Step 3 ✅;4.3 folder-tags → Task 2 Step 4 ✅。
- D2 即時樹不改 schema → 無 migration ✅;D3 遞迴 → `Fold` 後序 ✅;D7 distinct present → `HashSet`/`Distinct` + `Status=="present"` ✅。
- 前綴邊界(`Pixiv` 不中 `Pixiv2`)→ `FolderScopeQueryTests` 明確斷言 ✅。
- 無 placeholder;型別/簽名跨 Task 一致(`FolderNode`/`FolderTag`/`FolderTreeService`/`SearchAsync` 尾參)。
