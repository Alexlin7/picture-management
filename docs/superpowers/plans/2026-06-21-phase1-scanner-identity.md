# Phase 1 Scanner:身分與位置 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **前置:** 需先完成「Phase 1 地基」計畫(`2026-06-21-phase1-foundation.md`)—— 本計畫依賴 `PmDbContext`、`Photo`/`PhotoLocation`/`LibraryRoot` 實體、`Pm.Api` 宿主。

**Goal:** 把一個 `library_root` 指向的資料夾**就地索引**進 DB:走訪檔案 → 算 SHA-256 → 以 hash 為身分 upsert `photo`、以 (root, rel_path) upsert `photo_location`;同內容多檔自動去重(一個 `photo`、多個 `photo_location`);重掃時以 size+mtime 快路徑跳過沒變的檔案、不重算 hash。**絕不修改原始檔。**

**Architecture:** 新增 `Pm.Scanner` class library(依賴 `Pm.Data`),內含 `IFileHasher`(SHA-256 串流)與 `LibraryScanner`(走訪 + upsert)。`Pm.Api` 引用之,提供「新增 root」「觸發掃描」兩個端點。掃描為單程序內同步執行(背景排程化留後續計畫)。對齊 spec §5.1 的掃描/搬移偵測流程的**身分與位置部分**(對帳/失蹤判斷在計畫 3)。

**Tech Stack:** .NET 10、EF Core 10.x、`Microsoft.EntityFrameworkCore.Sqlite`、`System.Security.Cryptography.SHA256`、xUnit。

## Global Constraints

(沿用地基計畫的全專案鐵則,擇與本計畫相關者)

- **絕不修改/搬動/改名原始圖檔。** 掃描器**只讀**:`stat` + 串流讀取算 hash,不開寫入控制代碼。
- **`file_hash`(SHA-256,小寫 hex,64 字)是身分;`file_path` 只是位置。** 搬移/副本/去重一律靠 `photo_location`,`photo` 身分不動。
- **欄位命名 snake_case**。
- **單一程序、SQLite**:寫入由單程序序列化;掃描在 .NET 程序內。
- **API 只 bind `localhost`**。
- 本計畫引入一個 schema 演進:`photo_location.mtime`(快路徑用),同步更新 spec §4.2。

---

## File Structure

```
src/
├─ Pm.Scanner/                      # 新專案
│  ├─ Pm.Scanner.csproj             # ref Pm.Data
│  ├─ IFileHasher.cs
│  ├─ Sha256FileHasher.cs
│  ├─ ScanResult.cs
│  └─ LibraryScanner.cs
├─ Pm.Data/
│  ├─ Entities/PhotoLocation.cs     # +Mtime
│  ├─ PmDbContext.cs                # +mtime 映射
│  └─ Migrations/*                  # +AddLocationMtime
└─ Pm.Api/
   └─ Program.cs                    # +DI、+/api/roots、+/api/roots/{id}/scan
tests/
├─ Pm.Scanner.Tests/
│  ├─ Pm.Scanner.Tests.csproj
│  ├─ HasherTests.cs
│  └─ ScannerTests.cs
└─ Pm.Api.Tests/
   └─ RootScanTests.cs              # 端點:新增 root + 觸發掃描
```

---

## Task 1: `IFileHasher`(SHA-256 串流雜湊)

掃描器的身分計算單元:串流讀檔(只讀、不鎖寫)、回小寫 hex。獨立、純粹、易測。

**Files:**
- Create: `src/Pm.Scanner/Pm.Scanner.csproj`
- Create: `src/Pm.Scanner/IFileHasher.cs`
- Create: `src/Pm.Scanner/Sha256FileHasher.cs`
- Create: `tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj`
- Create: `tests/Pm.Scanner.Tests/HasherTests.cs`

**Interfaces:**
- Consumes: 無(可獨立於 DB)。
- Produces: `IFileHasher { Task<string> HashFileAsync(string absPath, CancellationToken ct = default); }`(回 64 字小寫 hex);`Sha256FileHasher` 實作。

- [ ] **Step 1: 建 Pm.Scanner 專案並接上 solution**

Run:

```bash
cd /d/picture-management
dotnet new classlib -n Pm.Scanner -o src/Pm.Scanner
dotnet sln add src/Pm.Scanner/Pm.Scanner.csproj
dotnet add src/Pm.Scanner/Pm.Scanner.csproj reference src/Pm.Data/Pm.Data.csproj
rm src/Pm.Scanner/Class1.cs
```

- [ ] **Step 2: 寫介面**

Create `src/Pm.Scanner/IFileHasher.cs`:

```csharp
namespace Pm.Scanner;

public interface IFileHasher
{
    /// <summary>串流讀檔算 SHA-256,回 64 字小寫 hex。只讀,不取得寫入控制代碼。</summary>
    Task<string> HashFileAsync(string absPath, CancellationToken ct = default);
}
```

- [ ] **Step 3: 寫實作**

Create `src/Pm.Scanner/Sha256FileHasher.cs`:

```csharp
using System.Security.Cryptography;

namespace Pm.Scanner;

public sealed class Sha256FileHasher : IFileHasher
{
    public async Task<string> HashFileAsync(string absPath, CancellationToken ct = default)
    {
        // FileShare.Read:允許他人同時讀;FileAccess.Read:我們絕不寫原檔。
        await using var fs = new FileStream(
            absPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: true);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexStringLower(hash);   // .NET 9+ 內建小寫 hex
    }
}
```

- [ ] **Step 4: 建測試專案並接上**

Run:

```bash
cd /d/picture-management
dotnet new xunit -n Pm.Scanner.Tests -o tests/Pm.Scanner.Tests
dotnet sln add tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj
dotnet add tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj reference src/Pm.Scanner/Pm.Scanner.csproj
dotnet add tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj reference src/Pm.Data/Pm.Data.csproj
dotnet add tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite
```

(Pm.Data + Sqlite 參考供 Task 3 的整合測試用,先一次加齊。)

- [ ] **Step 5: 寫失敗的雜湊測試**

用已知向量:`"abc"` 的 SHA-256 = `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`。

Create `tests/Pm.Scanner.Tests/HasherTests.cs`:

```csharp
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class HasherTests
{
    [Fact]
    public async Task Hashes_known_vector_abc()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pm-hash-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, "abc"u8.ToArray());
        try
        {
            var hash = await new Sha256FileHasher().HashFileAsync(path);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
            Assert.Equal(64, hash.Length);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 6: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj
```

Expected: PASS,1 passed。

- [ ] **Step 7: Commit**

```bash
cd /d/picture-management
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: Pm.Scanner + IFileHasher(SHA-256 串流,只讀原檔)"
```

---

## Task 2: schema 演進 —— `photo_location.mtime`(快路徑中繼)

快路徑要靠「上次掃描看到的檔案修改時間」判斷沒變。`photo_location` 加一欄 `mtime`(可空 —— 既有列無此值時自然不命中快路徑,退回重算)。

**Files:**
- Modify: `src/Pm.Data/Entities/PhotoLocation.cs`
- Modify: `src/Pm.Data/PmDbContext.cs`
- Create: `src/Pm.Data/Migrations/*_AddLocationMtime.cs`(`dotnet ef` 產生)
- Create: `tests/Pm.Scanner.Tests/MtimeSchemaTests.cs`

**Interfaces:**
- Consumes: 地基的 `PmDbContext`、`PhotoLocation`。
- Produces: `PhotoLocation.Mtime : DateTimeOffset?`(欄名 `mtime`)。

- [ ] **Step 1: 實體加 Mtime**

Modify `src/Pm.Data/Entities/PhotoLocation.cs` —— 在 `LastSeenAt` 後加一行:

```csharp
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? Mtime { get; set; }   // 上次掃描看到的檔案修改時間(快路徑用)
```

- [ ] **Step 2: DbContext 映射 mtime**

Modify `src/Pm.Data/PmDbContext.cs` —— 在 `PhotoLocation` 設定區塊,`LastSeenAt` 那行之後加:

```csharp
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.Mtime).HasColumnName("mtime");
```

- [ ] **Step 3: 產生 migration**

Run:

```bash
cd /d/picture-management
dotnet ef migrations add AddLocationMtime --project src/Pm.Data
```

Expected: 產生 `*_AddLocationMtime.cs`,內容為 `AddColumn`(`mtime`,nullable),印 "Done."。

- [ ] **Step 4: 寫失敗的 schema 測試**

驗證 migration 套得上、`mtime` 可往返。

Create `tests/Pm.Scanner.Tests/MtimeSchemaTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Scanner.Tests;

public class MtimeSchemaTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-mtime-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public MtimeSchemaTests()
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
    public async Task Location_round_trips_mtime()
    {
        var when = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);

        await using (var ctx = NewContext())
        {
            var root = new LibraryRoot { Name = "本機", AbsPath = @"D:\pics" };
            var photo = new Photo { FileHash = new string('a', 64), FileSize = 10 };
            photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = "a.png", Mtime = when });
            ctx.Photos.Add(photo);
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        var loc = await ctx2.PhotoLocations.SingleAsync();
        Assert.Equal(when, loc.Mtime);
    }
}
```

- [ ] **Step 5: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj
```

Expected: PASS(Task 1 的 1 + 本 task 的 1 = 2)。

- [ ] **Step 6: Commit**

```bash
cd /d/picture-management
git add src/Pm.Data tests/Pm.Scanner.Tests
git commit -m "feat: photo_location.mtime 欄位 + migration(快路徑中繼)"
```

---

## Task 3: `LibraryScanner` 核心 —— 走訪 + 雜湊 + upsert 身分/位置

掃描器主體:走訪 root 下的圖片檔,對每個檔以 hash upsert `photo`、以 (root, rel_path) upsert `photo_location`。本 task 聚焦**首次索引與去重**(快路徑在 Task 4)。

**Files:**
- Create: `src/Pm.Scanner/ScanResult.cs`
- Create: `src/Pm.Scanner/LibraryScanner.cs`
- Create: `tests/Pm.Scanner.Tests/ScannerTests.cs`

**Interfaces:**
- Consumes: `PmDbContext`、`IFileHasher`、`Photo`/`PhotoLocation`/`LibraryRoot`。
- Produces:
  - `record ScanResult(int FilesSeen, int NewPhotos, int NewLocations, int SkippedUnchanged, int Errors)`
  - `LibraryScanner(PmDbContext db, IFileHasher hasher)`,方法 `Task<ScanResult> ScanRootAsync(long rootId, CancellationToken ct = default)`

- [ ] **Step 1: 寫 ScanResult**

Create `src/Pm.Scanner/ScanResult.cs`:

```csharp
namespace Pm.Scanner;

/// <summary>一次掃描的統計。</summary>
public sealed record ScanResult(
    int FilesSeen,         // 看到的圖片檔總數
    int NewPhotos,         // 新身分(新 hash)
    int NewLocations,      // 新位置(新的 root+rel_path)
    int SkippedUnchanged,  // 快路徑跳過(size+mtime 沒變)
    int Errors);           // 讀取失敗略過
```

- [ ] **Step 2: 寫 LibraryScanner(本 task 先不含快路徑分支)**

Create `src/Pm.Scanner/LibraryScanner.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;

namespace Pm.Scanner;

public sealed class LibraryScanner(PmDbContext db, IFileHasher hasher)
{
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".avif", ".jfif"
    };

    public async Task<ScanResult> ScanRootAsync(long rootId, CancellationToken ct = default)
    {
        var root = await db.LibraryRoots.FindAsync([rootId], ct)
                   ?? throw new InvalidOperationException($"library_root {rootId} 不存在");

        int seen = 0, newPhotos = 0, newLocations = 0, skipped = 0, errors = 0;

        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(root.AbsPath, "*", opts))
        {
            ct.ThrowIfCancellationRequested();
            if (!ImageExts.Contains(Path.GetExtension(file))) continue;
            seen++;

            try
            {
                var info = new FileInfo(file);
                var relPath = Path.GetRelativePath(root.AbsPath, file).Replace('\\', '/');
                var size = info.Length;
                var mtime = (DateTimeOffset)info.LastWriteTimeUtc;

                var loc = await db.PhotoLocations
                    .Include(l => l.Photo)
                    .FirstOrDefaultAsync(l => l.LibraryRootId == rootId && l.RelPath == relPath, ct);

                // Task 4 會在此插入快路徑;本 task 一律重算 hash。

                var hash = await hasher.HashFileAsync(file, ct);

                var photo = await db.Photos.FirstOrDefaultAsync(p => p.FileHash == hash, ct);
                if (photo is null)
                {
                    photo = new Photo { FileHash = hash, FileSize = size };
                    db.Photos.Add(photo);
                    await db.SaveChangesAsync(ct);   // 取得 photo.Id
                    newPhotos++;
                }

                if (loc is null)
                {
                    db.PhotoLocations.Add(new PhotoLocation
                    {
                        PhotoId = photo.Id,
                        LibraryRootId = rootId,
                        RelPath = relPath,
                        Status = "present",
                        Mtime = mtime,
                        FirstSeenAt = DateTimeOffset.UtcNow,
                        LastSeenAt = DateTimeOffset.UtcNow,
                    });
                    newLocations++;
                }
                else
                {
                    // 既有位置但內容變了 → 指向(可能是新的)photo,更新中繼。
                    loc.PhotoId = photo.Id;
                    loc.Status = "present";
                    loc.Mtime = mtime;
                    loc.LastSeenAt = DateTimeOffset.UtcNow;
                }

                await db.SaveChangesAsync(ct);
            }
            catch (IOException) { errors++; }
            catch (UnauthorizedAccessException) { errors++; }
        }

        return new ScanResult(seen, newPhotos, newLocations, skipped, errors);
    }
}
```

- [ ] **Step 3: 寫失敗的掃描整合測試(首次索引 + 去重)**

建暫存資料夾與暫存 SQLite,放兩個**不同內容**檔 + 一個與其一**相同內容**的副本,驗證:新身分數、位置數、去重(同 hash 共用一個 `photo`)。

Create `tests/Pm.Scanner.Tests/ScannerTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class ScannerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-scan-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-root-{Guid.NewGuid():N}");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public ScannerTests()
    {
        Directory.CreateDirectory(_root);
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    private async Task<long> SeedRootAsync()
    {
        await using var ctx = NewContext();
        var root = new LibraryRoot { Name = "test", AbsPath = _root };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        return root.Id;
    }

    private void WriteImage(string relPath, string content)
    {
        var full = Path.Combine(_root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);   // 副檔名是 .png,內容當作 bytes 即可(本階段不解析圖)
    }

    [Fact]
    public async Task First_scan_indexes_and_dedups_by_hash()
    {
        WriteImage("a.png", "alpha");
        WriteImage("sub/b.png", "beta");
        WriteImage("sub/b_copy.png", "beta");   // 與 b.png 同內容 → 同 hash
        var rootId = await SeedRootAsync();

        await using var ctx = NewContext();
        var scanner = new LibraryScanner(ctx, new Sha256FileHasher());
        var result = await scanner.ScanRootAsync(rootId);

        Assert.Equal(3, result.FilesSeen);
        Assert.Equal(2, result.NewPhotos);       // alpha、beta 兩個身分
        Assert.Equal(3, result.NewLocations);    // 三個實體位置

        await using var verify = NewContext();
        Assert.Equal(2, await verify.Photos.CountAsync());
        Assert.Equal(3, await verify.PhotoLocations.CountAsync());

        // beta 的 photo 掛兩個位置
        var beta = await verify.Photos.Include("Locations")
            .SingleAsync(p => p.Locations.Count == 2);
        Assert.All(beta.Locations, l => Assert.Equal("present", l.Status));
    }

    [Fact]
    public async Task Missing_root_throws()
    {
        await using var ctx = NewContext();
        var scanner = new LibraryScanner(ctx, new Sha256FileHasher());
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanRootAsync(999));
    }
}
```

- [ ] **Step 4: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj
```

Expected: PASS(Task 1–2 的 2 + 本 task 的 2 = 4)。

- [ ] **Step 5: Commit**

```bash
cd /d/picture-management
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: LibraryScanner 核心(走訪+SHA-256+upsert photo/location,hash 去重)"
```

---

## Task 4: 快路徑 + 內容變更 + 冪等

重掃效率與正確性:同檔沒變 → 不重算 hash(快路徑);內容變了 → 重算並換身分;整體重掃冪等(不產生重複位置)。

**Files:**
- Modify: `src/Pm.Scanner/LibraryScanner.cs`(加入快路徑分支)
- Modify: `tests/Pm.Scanner.Tests/ScannerTests.cs`(加重掃測試)

**Interfaces:**
- Consumes / Produces:同 Task 3(行為強化,簽章不變)。

- [ ] **Step 1: 寫失敗的重掃測試**

在 `ScannerTests.cs` 加三個測試:快路徑跳過、內容變更換身分、重掃冪等。

Add to `tests/Pm.Scanner.Tests/ScannerTests.cs`(類別內,`Missing_root_throws` 之後):

```csharp
    [Fact]
    public async Task Rescan_unchanged_uses_fast_path()
    {
        WriteImage("a.png", "alpha");
        var rootId = await SeedRootAsync();

        await using (var ctx = NewContext())
            await new LibraryScanner(ctx, new Sha256FileHasher()).ScanRootAsync(rootId);

        // 第二輪:檔案沒動 → 應走快路徑、不新增身分/位置
        await using var ctx2 = NewContext();
        var counting = new CountingHasher(new Sha256FileHasher());
        var result = await new LibraryScanner(ctx2, counting).ScanRootAsync(rootId);

        Assert.Equal(1, result.FilesSeen);
        Assert.Equal(0, result.NewPhotos);
        Assert.Equal(0, result.NewLocations);
        Assert.Equal(1, result.SkippedUnchanged);
        Assert.Equal(0, counting.Calls);          // 關鍵:沒重算 hash
    }

    [Fact]
    public async Task Changed_content_rehashes_and_reassigns_identity()
    {
        WriteImage("a.png", "alpha");
        var rootId = await SeedRootAsync();
        await using (var ctx = NewContext())
            await new LibraryScanner(ctx, new Sha256FileHasher()).ScanRootAsync(rootId);

        // 改內容 + 推進 mtime
        var full = Path.Combine(_root, "a.png");
        await File.WriteAllTextAsync(full, "ALPHA-v2");
        File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddMinutes(5));

        await using var ctx2 = NewContext();
        var result = await new LibraryScanner(ctx2, new Sha256FileHasher()).ScanRootAsync(rootId);

        Assert.Equal(1, result.NewPhotos);        // 新內容 = 新身分
        Assert.Equal(0, result.NewLocations);     // 還是同一個位置,只是換了 photo
        Assert.Equal(0, result.SkippedUnchanged);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.PhotoLocations.CountAsync());   // 位置沒重複
        Assert.Equal(2, await verify.Photos.CountAsync());           // 舊+新兩個身分
        var loc = await verify.PhotoLocations.Include(l => l.Photo).SingleAsync();
        Assert.Equal("ALPHA-v2".Length, loc.Photo.FileSize);         // 指向新身分
    }

    [Fact]
    public async Task Rescan_is_idempotent()
    {
        WriteImage("a.png", "alpha");
        WriteImage("b.png", "beta");
        var rootId = await SeedRootAsync();

        for (int i = 0; i < 3; i++)
            await using (var ctx = NewContext())
                await new LibraryScanner(ctx, new Sha256FileHasher()).ScanRootAsync(rootId);

        await using var verify = NewContext();
        Assert.Equal(2, await verify.Photos.CountAsync());
        Assert.Equal(2, await verify.PhotoLocations.CountAsync());
    }
```

在檔案末端(類別外)加一個計數用的 hasher 包裝:

```csharp
file sealed class CountingHasher(IFileHasher inner) : IFileHasher
{
    public int Calls { get; private set; }
    public Task<string> HashFileAsync(string absPath, CancellationToken ct = default)
    {
        Calls++;
        return inner.HashFileAsync(absPath, ct);
    }
}
```

- [ ] **Step 2: 跑測試,確認先失敗**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj
```

Expected: FAIL —— `Rescan_unchanged_uses_fast_path` 失敗(目前一律重算,`counting.Calls` = 1 而非 0、`SkippedUnchanged` = 0)。

- [ ] **Step 3: 在 LibraryScanner 加入快路徑分支**

Modify `src/Pm.Scanner/LibraryScanner.cs` —— 把 Task 3 留的註解那行:

```csharp
                // Task 4 會在此插入快路徑;本 task 一律重算 hash。
```

替換為:

```csharp
                // 快路徑:同位置、present、size 與 mtime 都沒變(容 1 秒誤差,跨檔系統 mtime 精度不一)→ 不重算 hash。
                if (loc is { Status: "present", Mtime: { } prevMtime }
                    && loc.Photo.FileSize == size
                    && (prevMtime - mtime).Duration() < TimeSpan.FromSeconds(1))
                {
                    loc.LastSeenAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                    skipped++;
                    continue;
                }
```

- [ ] **Step 4: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj
```

Expected: PASS(共 7:Task1–3 的 4 + 本 task 的 3)。

- [ ] **Step 5: Commit**

```bash
cd /d/picture-management
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: 掃描快路徑(size+mtime 跳過)+ 內容變更換身分 + 重掃冪等"
```

---

## Task 5: API —— 新增 library_root + 觸發掃描

把掃描器接上 HTTP,讓「指一個資料夾 → 進庫」可由前端/curl 操作。

**Files:**
- Modify: `src/Pm.Api/Program.cs`
- Modify: `src/Pm.Api/Pm.Api.csproj`(引用 Pm.Scanner)
- Create: `tests/Pm.Api.Tests/RootScanTests.cs`

**Interfaces:**
- Consumes: `LibraryScanner`、`IFileHasher`、`PmDbContext`。
- Produces:
  - `POST /api/roots`,body `{ "name": string, "absPath": string }` → `201` `{ id, name, absPath }`
  - `POST /api/roots/{id:long}/scan` → `200` `ScanResult`(JSON)

- [ ] **Step 1: Pm.Api 引用 Pm.Scanner**

Run:

```bash
cd /d/picture-management
dotnet add src/Pm.Api/Pm.Api.csproj reference src/Pm.Scanner/Pm.Scanner.csproj
```

- [ ] **Step 2: 註冊服務與端點**

Modify `src/Pm.Api/Program.cs` —— 在 `builder.Services.AddDbContext...` 之後加註冊:

```csharp
builder.Services.AddDbContext<PmDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Pm")));

builder.Services.AddScoped<IFileHasher, Sha256FileHasher>();
builder.Services.AddScoped<LibraryScanner>();
```

並在 `var app = builder.Build();` 與 `app.Run();` 之間(健康檢查端點附近)加:

```csharp
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
```

並在檔案頂端補 using 與檔末的 DTO(`public partial class Program { }` 之前):

```csharp
using Pm.Data.Entities;
using Pm.Scanner;
```

```csharp
public record CreateRootDto(string Name, string AbsPath);
```

- [ ] **Step 3: 寫失敗的端點測試**

用覆寫連線字串的 `WebApplicationFactory`(各測試獨立的暫存 DB),建一個暫存資料夾放圖,POST 建 root → POST 觸發掃描 → 驗證回傳統計。

Create `tests/Pm.Api.Tests/RootScanTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Pm.Api.Tests;

public class RootScanTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-apidb-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-apiroot-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public RootScanTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "a.png"), "alpha");
        File.WriteAllText(Path.Combine(_root, "b.png"), "beta");

        var dbPath = _dbPath;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Pm"] = $"Data Source={dbPath};Foreign Keys=True"
                })));
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private record RootCreated(long Id, string Name, string AbsPath);
    private record ScanDto(int FilesSeen, int NewPhotos, int NewLocations, int SkippedUnchanged, int Errors);

    [Fact]
    public async Task Create_root_then_scan_indexes_files()
    {
        var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/roots", new { name = "test", absPath = _root });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var root = await create.Content.ReadFromJsonAsync<RootCreated>();
        Assert.NotNull(root);

        var scan = await client.PostAsync($"/api/roots/{root!.Id}/scan", null);
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        var result = await scan.Content.ReadFromJsonAsync<ScanDto>();

        Assert.Equal(2, result!.FilesSeen);
        Assert.Equal(2, result.NewPhotos);
        Assert.Equal(2, result.NewLocations);
    }
}
```

- [ ] **Step 4: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj
```

Expected: PASS(地基的 2 健康檢查 + 本 task 的 1 = 3)。

- [ ] **Step 5: 手動煙霧測試**

Run:

```bash
cd /d/picture-management
mkdir -p /tmp/pmsmoke && echo one > /tmp/pmsmoke/x.png && echo two > /tmp/pmsmoke/y.png
dotnet run --project src/Pm.Api &
sleep 4
RID=$(curl -s -X POST http://localhost:5180/api/roots -H "Content-Type: application/json" -d "{\"name\":\"smoke\",\"absPath\":\"/tmp/pmsmoke\"}" | python -c "import sys,json;print(json.load(sys.stdin)['id'])")
curl -s -X POST http://localhost:5180/api/roots/$RID/scan
kill %1
```

Expected:scan 回 `{"filesSeen":2,"newPhotos":2,"newLocations":2,"skippedUnchanged":0,"errors":0}`。

- [ ] **Step 6: 全 solution 總驗收**

Run:

```bash
cd /d/picture-management
dotnet test
```

Expected: 全綠 —— 地基 15 + Scanner 7 + API 新增 1 = **23 passed**。

- [ ] **Step 7: Commit**

```bash
cd /d/picture-management
git add src/Pm.Api tests/Pm.Api.Tests
git commit -m "feat: API 新增 library_root + 觸發掃描端點(指資料夾→進庫)"
```

---

## 完成定義(Scanner 身分與位置)

- `IFileHasher` 串流算 SHA-256,只讀原檔(`FileAccess.Read`/`FileShare.Read`)。
- `POST /api/roots` 建來源、`POST /api/roots/{id}/scan` 觸發掃描,回統計。
- 首次掃描:圖片檔 → `photo`(hash 身分)+ `photo_location`(位置);同內容多檔去重成一個 `photo`、多個 `photo_location`。
- 重掃:size+mtime 沒變走快路徑、不重算 hash;內容變更重算並換身分;整體冪等。
- 讀取失敗的檔案略過計數,不阻斷整批。
- `dotnet test` 全綠(23 passed)。

**明確不在本計畫**(留計畫 3):EXIF/尺寸抽取、縮圖、搬移/失蹤對帳(這輪沒看到的位置如何標 missing/archived)、塞 `tagging_job`。本計畫只負責「身分 + 位置」這兩層的就地索引。

---

## Self-Review 註記

- **Spec 覆蓋:** 對應 §5.1 掃描流程的**身分/位置**部分(stat → hash → upsert photo/location、快路徑 size+mtime);§4.3 身分/位置兩層;鐵則「只讀原檔」「hash 為身分」。**對帳/失蹤判斷**(§5.1 後半)明確延後至計畫 3。
- **Schema 演進:** 新增 `photo_location.mtime`,同步更新 spec §4.2。
- **無 placeholder:** 每步皆可執行指令或完整程式碼。
- **型別一致:** `ScanResult` 欄位、`ScanRootAsync` 簽章、`PhotoLocation.Mtime`、端點 DTO 在各 Task 的 Interfaces 與測試中一致。
