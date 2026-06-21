# Phase 1 Scanner:EXIF + 縮圖 + 對帳 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **前置:** 需先完成「地基」與「Scanner 身分與位置」兩份計畫。本計畫**延伸** `LibraryScanner`(身分新建時補 EXIF/尺寸/縮圖/排 job;走訪後對帳)。

**Goal:** 把掃描器從「只記身分與位置」升級到完整就地索引:新身分建立時抽 EXIF/尺寸/MIME、產 webp 縮圖(衍生快取、絕不碰原圖)、排入 `tagging_job` 給 WD14;走訪結束後**對帳** —— 這輪沒看到的位置標 `missing`(搬移=仍有別處 present 不打擾;真失蹤=整張圖無 present 位置,進待確認匣);同 hash 回來自動復原。

**Architecture:** 在 `Pm.Scanner` 加兩個服務:`IImageMetadataReader`(ImageSharp 取尺寸/MIME + MetadataExtractor 取 EXIF)與 `IThumbnailService`(ImageSharp 產 512px webp,依 hash 分桶)。`LibraryScanner` 注入這兩者(加一個**便利建構子**讓既有測試免改),在「新 photo」分支補 metadata + 縮圖 + job,並在走訪後做對帳(EF `ExecuteUpdate` 標 missing)。`Pm.Api` 加 DI 與待確認匣讀取端點。

**Tech Stack:** .NET 10、EF Core 10.x、`SixLabors.ImageSharp`、`MetadataExtractor`、xUnit。

## Global Constraints

- **絕不修改/搬動/改名原始圖檔,絕不寫 XMP。** EXIF 只讀;縮圖寫進 **app 自有快取目錄**(依 hash 分桶),不碰原圖。
- **盡力而為**:EXIF/縮圖失敗不得讓索引失敗 —— 壞圖/非圖仍保留身分與位置,只是無尺寸/無縮圖/不排 job。
- **`file_hash` 是身分**;EXIF/尺寸/縮圖/`tagging_job` 都掛在身分上,**只在新 hash 首次出現時做一次**(同內容換位置不重做)。
- **刪除是軟刪**:對帳只把消失的位置標 `missing`,**保留 `photo` 與 tags**;同 hash 回來自動復原。硬刪 purge 需使用者明示(不在本計畫)。
- **DB-as-queue 程序內**:`tagging_job` 是工作清單;本計畫只負責「塞」。

---

## File Structure

```
src/
├─ Pm.Scanner/
│  ├─ Pm.Scanner.csproj             # +SixLabors.ImageSharp +MetadataExtractor
│  ├─ ImageMeta.cs                  # record struct
│  ├─ IImageMetadataReader.cs
│  ├─ ExifImageMetadataReader.cs    # ImageSharp 尺寸/MIME + MetadataExtractor EXIF
│  ├─ ThumbnailOptions.cs
│  ├─ IThumbnailService.cs
│  ├─ ThumbnailService.cs           # 512px webp,依 hash 分桶
│  ├─ ScanResult.cs                 # +ThumbsGenerated/JobsQueued/MarkedMissing
│  └─ LibraryScanner.cs             # +便利建構子、新 photo 補 meta/thumb/job、走訪後對帳
└─ Pm.Api/
   ├─ Program.cs                    # +DI(reader/thumb/options)、+GET /api/reconcile/missing
   └─ appsettings.json              # +Thumbnails 區段
tests/
├─ Pm.Scanner.Tests/
│  ├─ MetadataReaderTests.cs
│  ├─ ThumbnailServiceTests.cs
│  ├─ EnrichTests.cs                # 掃描補 meta/thumb/job
│  └─ ReconcileTests.cs             # 失蹤/搬移/復原
└─ Pm.Api.Tests/
   └─ ReconcileApiTests.cs          # 待確認匣端點
```

---

## Task 1: `IImageMetadataReader`(尺寸/MIME + EXIF)

讀一張圖的尺寸、MIME、EXIF(拍攝時間/相機/GPS)+ 一份 EXIF JSON。全程只讀;不可解碼時回 null 欄位、不丟例外給呼叫端。

**Files:**
- Create: `src/Pm.Scanner/ImageMeta.cs`
- Create: `src/Pm.Scanner/IImageMetadataReader.cs`
- Create: `src/Pm.Scanner/ExifImageMetadataReader.cs`
- Create: `tests/Pm.Scanner.Tests/MetadataReaderTests.cs`

**Interfaces:**
- Consumes: 無。
- Produces:
  - `readonly record struct ImageMeta(int? Width, int? Height, string? Mime, DateTimeOffset? TakenAt, string? CameraModel, double? GpsLat, double? GpsLon, string? ExifJson)`
  - `IImageMetadataReader { ImageMeta Read(string absPath); }`
  - `ExifImageMetadataReader` 實作。

- [ ] **Step 1: 裝套件**

Run:

```bash
cd /d/picture-management
dotnet add src/Pm.Scanner/Pm.Scanner.csproj package SixLabors.ImageSharp
dotnet add src/Pm.Scanner/Pm.Scanner.csproj package MetadataExtractor
```

- [ ] **Step 2: 寫 ImageMeta 與介面**

Create `src/Pm.Scanner/ImageMeta.cs`:

```csharp
namespace Pm.Scanner;

public readonly record struct ImageMeta(
    int? Width,
    int? Height,
    string? Mime,
    DateTimeOffset? TakenAt,
    string? CameraModel,
    double? GpsLat,
    double? GpsLon,
    string? ExifJson);
```

Create `src/Pm.Scanner/IImageMetadataReader.cs`:

```csharp
namespace Pm.Scanner;

public interface IImageMetadataReader
{
    /// <summary>只讀。無法解碼/無 EXIF 時對應欄位回 null,不丟例外。</summary>
    ImageMeta Read(string absPath);
}
```

- [ ] **Step 3: 寫實作**

Create `src/Pm.Scanner/ExifImageMetadataReader.cs`:

```csharp
using System.Text.Json;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SixLabors.ImageSharp;

namespace Pm.Scanner;

public sealed class ExifImageMetadataReader : IImageMetadataReader
{
    public ImageMeta Read(string absPath)
    {
        int? width = null, height = null;
        string? mime = null;

        // 尺寸 / MIME(ImageSharp)
        try
        {
            var info = Image.Identify(absPath);
            width = info.Width;
            height = info.Height;
            mime = info.Metadata.DecodedImageFormat?.DefaultMimeType;
        }
        catch { /* 非 ImageSharp 可解碼的圖 */ }

        DateTimeOffset? takenAt = null;
        string? cameraModel = null;
        double? gpsLat = null, gpsLon = null;
        string? exifJson = null;

        // EXIF(MetadataExtractor)
        try
        {
            var dirs = MetadataExtractor.ImageMetadataReader.ReadMetadata(absPath);
            var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            var sub = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var gps = dirs.OfType<GpsDirectory>().FirstOrDefault();

            var make = ifd0?.GetDescription(ExifDirectoryBase.TagMake);
            var model = ifd0?.GetDescription(ExifDirectoryBase.TagModel);
            var combined = string.Join(" ",
                new[] { make, model }.Where(s => !string.IsNullOrWhiteSpace(s)));
            cameraModel = string.IsNullOrWhiteSpace(combined) ? null : combined;

            if (sub is not null &&
                sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                takenAt = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));

            var geo = gps?.GetGeoLocation();
            if (geo is not null && !geo.IsZero)
            {
                gpsLat = geo.Latitude;
                gpsLon = geo.Longitude;
            }

            var map = new Dictionary<string, string>();
            foreach (var d in dirs)
                foreach (var t in d.Tags)
                    if (t.Description is not null)
                        map[$"{d.Name}/{t.Name}"] = t.Description;
            if (map.Count > 0)
                exifJson = JsonSerializer.Serialize(map);
        }
        catch { /* 無/壞 EXIF */ }

        return new ImageMeta(width, height, mime, takenAt, cameraModel, gpsLat, gpsLon, exifJson);
    }
}
```

- [ ] **Step 4: 裝測試專案的 ImageSharp(供產生測試圖)**

Run:

```bash
cd /d/picture-management
dotnet add tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj package SixLabors.ImageSharp
```

- [ ] **Step 5: 寫失敗的測試**

(a) 產一張純 PNG → 尺寸/MIME 對、EXIF 欄位 null;(b) 產一張帶 EXIF 的 JPEG(ImageSharp 寫入 Make/Model/DateTimeOriginal)→ 相機/拍攝時間讀得到;(c) 非圖內容 → 全 null 不丟例外。

Create `tests/Pm.Scanner.Tests/MetadataReaderTests.cs`:

```csharp
using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Scanner.Tests;

public class MetadataReaderTests
{
    private static string Temp(string ext) =>
        Path.Combine(Path.GetTempPath(), $"pm-meta-{Guid.NewGuid():N}{ext}");

    [Fact]
    public async Task Reads_dimensions_and_mime_for_png()
    {
        var path = Temp(".png");
        using (var img = new Image<Rgba32>(800, 600)) await img.SaveAsPngAsync(path);
        try
        {
            var meta = new ExifImageMetadataReader().Read(path);
            Assert.Equal(800, meta.Width);
            Assert.Equal(600, meta.Height);
            Assert.Equal("image/png", meta.Mime);
            Assert.Null(meta.TakenAt);
            Assert.Null(meta.CameraModel);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Reads_camera_and_date_from_jpeg_exif()
    {
        var path = Temp(".jpg");
        using (var img = new Image<Rgba32>(64, 64))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Make, "Canon");
            exif.SetValue(ExifTag.Model, "EOS R5");
            exif.SetValue(ExifTag.DateTimeOriginal, "2026:06:21 10:30:00");
            img.Metadata.ExifProfile = exif;
            await img.SaveAsJpegAsync(path);
        }
        try
        {
            var meta = new ExifImageMetadataReader().Read(path);
            Assert.Equal("image/jpeg", meta.Mime);
            Assert.Contains("Canon", meta.CameraModel);
            Assert.Contains("EOS R5", meta.CameraModel);
            Assert.NotNull(meta.TakenAt);
            Assert.Equal(2026, meta.TakenAt!.Value.Year);
            Assert.NotNull(meta.ExifJson);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Non_image_returns_all_null_without_throwing()
    {
        var path = Temp(".png");
        await File.WriteAllTextAsync(path, "not really an image");
        try
        {
            var meta = new ExifImageMetadataReader().Read(path);
            Assert.Null(meta.Width);
            Assert.Null(meta.Mime);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 6: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter MetadataReaderTests
```

Expected: PASS,3 passed。

- [ ] **Step 7: Commit**

```bash
cd /d/picture-management
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: IImageMetadataReader(ImageSharp 尺寸/MIME + MetadataExtractor EXIF)"
```

---

## Task 2: `IThumbnailService`(512px webp,依 hash 分桶)

從原圖產縮圖寫進快取目錄,路徑依 hash 分桶(`<dir>/ab/cd/<hash>.webp`)。衍生、可重建、絕不碰原圖。

**Files:**
- Create: `src/Pm.Scanner/ThumbnailOptions.cs`
- Create: `src/Pm.Scanner/IThumbnailService.cs`
- Create: `src/Pm.Scanner/ThumbnailService.cs`
- Create: `tests/Pm.Scanner.Tests/ThumbnailServiceTests.cs`

**Interfaces:**
- Consumes: 無。
- Produces:
  - `ThumbnailOptions { string Dir = "thumbs"; int MaxEdge = 512; }`
  - `IThumbnailService { string PathFor(string hash); Task<string?> GenerateAsync(string absPath, string hash, CancellationToken ct = default); }`
  - `ThumbnailService(ThumbnailOptions options)` 實作。

- [ ] **Step 1: 寫 options 與介面**

Create `src/Pm.Scanner/ThumbnailOptions.cs`:

```csharp
namespace Pm.Scanner;

public sealed class ThumbnailOptions
{
    public string Dir { get; set; } = "thumbs";
    public int MaxEdge { get; set; } = 512;
}
```

Create `src/Pm.Scanner/IThumbnailService.cs`:

```csharp
namespace Pm.Scanner;

public interface IThumbnailService
{
    /// <summary>依 hash 分桶的縮圖檔路徑(不保證已存在)。</summary>
    string PathFor(string hash);

    /// <summary>產縮圖到 PathFor(hash)。成功回路徑,失敗回 null。只讀原圖。</summary>
    Task<string?> GenerateAsync(string absPath, string hash, CancellationToken ct = default);
}
```

- [ ] **Step 2: 寫實作**

Create `src/Pm.Scanner/ThumbnailService.cs`:

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Pm.Scanner;

public sealed class ThumbnailService(ThumbnailOptions options) : IThumbnailService
{
    public string PathFor(string hash) =>
        Path.Combine(options.Dir, hash[..2], hash[2..4], hash + ".webp");

    public async Task<string?> GenerateAsync(string absPath, string hash, CancellationToken ct = default)
    {
        var outPath = PathFor(hash);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        using var img = await Image.LoadAsync(absPath, ct);   // 只讀原圖
        img.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,                            // 保持比例,長邊不超過 MaxEdge
            Size = new Size(options.MaxEdge, options.MaxEdge),
        }));
        await img.SaveAsWebpAsync(outPath, ct);
        return outPath;
    }
}
```

- [ ] **Step 3: 寫失敗的測試**

Create `tests/Pm.Scanner.Tests/ThumbnailServiceTests.cs`:

```csharp
using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Scanner.Tests;

public class ThumbnailServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-thumbs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void PathFor_buckets_by_hash_prefix()
    {
        var svc = new ThumbnailService(new ThumbnailOptions { Dir = _dir });
        var hash = new string('a', 60) + "bcde";   // 64 字
        var p = svc.PathFor(hash);
        Assert.Equal(Path.Combine(_dir, "aa", "aa", hash + ".webp"), p);
    }

    [Fact]
    public async Task Generates_downscaled_webp()
    {
        var src = Path.Combine(_dir, "src.png");
        Directory.CreateDirectory(_dir);
        using (var img = new Image<Rgba32>(1024, 768)) await img.SaveAsPngAsync(src);

        var svc = new ThumbnailService(new ThumbnailOptions { Dir = _dir, MaxEdge = 512 });
        var hash = "abcd" + new string('0', 60);
        var outPath = await svc.GenerateAsync(src, hash);

        Assert.NotNull(outPath);
        Assert.True(File.Exists(outPath));
        Assert.Equal(svc.PathFor(hash), outPath);

        var info = Image.Identify(outPath!);
        Assert.Equal(512, info.Width);    // 長邊縮到 512
        Assert.Equal(384, info.Height);   // 比例保持(1024:768 → 512:384)
    }
}
```

- [ ] **Step 4: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter ThumbnailServiceTests
```

Expected: PASS,2 passed。

- [ ] **Step 5: Commit**

```bash
cd /d/picture-management
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: IThumbnailService(512px webp,依 hash 分桶,只讀原圖)"
```

---

## Task 3: 掃描整合 —— 新身分補 metadata + 縮圖 + 排 job

把 reader 與 thumb 接進 `LibraryScanner`:**新 photo** 建立時補尺寸/EXIF、產縮圖、塞 `tagging_job`(僅可解碼的圖)。加便利建構子讓既有測試免改。

**Files:**
- Modify: `src/Pm.Scanner/ScanResult.cs`
- Modify: `src/Pm.Scanner/LibraryScanner.cs`
- Modify: `src/Pm.Api/Program.cs`(DI 註冊 reader/thumb/options)
- Modify: `src/Pm.Api/appsettings.json`(Thumbnails 區段)
- Create: `tests/Pm.Scanner.Tests/EnrichTests.cs`

**Interfaces:**
- Consumes: Task 1 `IImageMetadataReader`、Task 2 `IThumbnailService`、`TaggingJob`。
- Produces:
  - `ScanResult` 加三欄:`int ThumbsGenerated, int JobsQueued, int MarkedMissing`(`MarkedMissing` 在 Task 4 才非零,先佔位)。
  - `LibraryScanner` 主建構子改為 `(PmDbContext db, IFileHasher hasher, IImageMetadataReader meta, IThumbnailService thumbs)`,並提供便利建構子 `(PmDbContext db, IFileHasher hasher)`。

- [ ] **Step 1: ScanResult 加欄位**

Overwrite `src/Pm.Scanner/ScanResult.cs`:

```csharp
namespace Pm.Scanner;

public sealed record ScanResult(
    int FilesSeen,
    int NewPhotos,
    int NewLocations,
    int SkippedUnchanged,
    int Errors,
    int ThumbsGenerated,
    int JobsQueued,
    int MarkedMissing);
```

- [ ] **Step 2: LibraryScanner 改建構子並在新 photo 分支補 enrich**

在 `src/Pm.Scanner/LibraryScanner.cs`:

a. 把類別宣告(primary constructor)改成四參數,並加便利建構子。將:

```csharp
public sealed class LibraryScanner(PmDbContext db, IFileHasher hasher)
{
```

改為:

```csharp
public sealed class LibraryScanner(
    PmDbContext db, IFileHasher hasher, IImageMetadataReader meta, IThumbnailService thumbs)
{
    // 便利建構子:既有呼叫端(只給 db+hasher)沿用預設 reader/thumb。
    public LibraryScanner(PmDbContext db, IFileHasher hasher)
        : this(db, hasher, new ExifImageMetadataReader(), new ThumbnailService(new ThumbnailOptions())) { }
```

b. 在計數器宣告處加 `thumbsGen`、`jobsQueued`、`markedMissing`。將:

```csharp
        int seen = 0, newPhotos = 0, newLocations = 0, skipped = 0, errors = 0;
```

改為:

```csharp
        int seen = 0, newPhotos = 0, newLocations = 0, skipped = 0, errors = 0;
        int thumbsGen = 0, jobsQueued = 0, markedMissing = 0;
```

c. 把「新 photo」分支補上 enrich。將:

```csharp
                if (photo is null)
                {
                    photo = new Photo { FileHash = hash, FileSize = size };
                    db.Photos.Add(photo);
                    await db.SaveChangesAsync(ct);   // 取得 photo.Id
                    newPhotos++;
                }
```

改為:

```csharp
                if (photo is null)
                {
                    photo = new Photo { FileHash = hash, FileSize = size };

                    var m = meta.Read(file);
                    photo.Width = m.Width;
                    photo.Height = m.Height;
                    photo.Mime = m.Mime;
                    photo.TakenAt = m.TakenAt;
                    photo.CameraModel = m.CameraModel;
                    photo.GpsLat = m.GpsLat;
                    photo.GpsLon = m.GpsLon;
                    photo.Exif = m.ExifJson;

                    db.Photos.Add(photo);
                    await db.SaveChangesAsync(ct);   // 取得 photo.Id
                    newPhotos++;

                    // 只有可解碼的圖才產縮圖 + 排 WD14(壞圖/非圖留身分但不做)
                    if (m.Width is not null)
                    {
                        try
                        {
                            await thumbs.GenerateAsync(file, hash, ct);
                            thumbsGen++;
                        }
                        catch { /* 縮圖失敗不影響索引 */ }

                        db.TaggingJobs.Add(new TaggingJob { PhotoId = photo.Id });
                        await db.SaveChangesAsync(ct);
                        jobsQueued++;
                    }
                }
```

d. 更新 return。將:

```csharp
        return new ScanResult(seen, newPhotos, newLocations, skipped, errors);
```

改為:

```csharp
        return new ScanResult(seen, newPhotos, newLocations, skipped, errors,
            thumbsGen, jobsQueued, markedMissing);
```

- [ ] **Step 3: API 註冊服務 + Thumbnails 設定**

在 `src/Pm.Api/Program.cs`,把原本的:

```csharp
builder.Services.AddScoped<IFileHasher, Sha256FileHasher>();
builder.Services.AddScoped<LibraryScanner>();
```

改為:

```csharp
var thumbOptions = builder.Configuration.GetSection("Thumbnails").Get<ThumbnailOptions>()
    ?? new ThumbnailOptions();
builder.Services.AddSingleton(thumbOptions);
builder.Services.AddScoped<IFileHasher, Sha256FileHasher>();
builder.Services.AddScoped<IImageMetadataReader, ExifImageMetadataReader>();
builder.Services.AddScoped<IThumbnailService, ThumbnailService>();
builder.Services.AddScoped<LibraryScanner>();
```

並在 `src/Pm.Api/appsettings.json` 根物件加一段(與 `ConnectionStrings` 同層):

```json
  "Thumbnails": {
    "Dir": "thumbs",
    "MaxEdge": 512
  },
```

- [ ] **Step 4: 寫失敗的整合測試**

Create `tests/Pm.Scanner.Tests/EnrichTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Scanner.Tests;

public class EnrichTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-enrich-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-enrichroot-{Guid.NewGuid():N}");
    private readonly string _thumbs = Path.Combine(Path.GetTempPath(), $"pm-enrichthumbs-{Guid.NewGuid():N}");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public EnrichTests()
    {
        Directory.CreateDirectory(_root);
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        foreach (var d in new[] { _root, _thumbs }) if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    private LibraryScanner Scanner(PmDbContext ctx) =>
        new(ctx, new Sha256FileHasher(), new ExifImageMetadataReader(),
            new ThumbnailService(new ThumbnailOptions { Dir = _thumbs }));

    private async Task<long> SeedRoot()
    {
        await using var ctx = NewContext();
        var root = new LibraryRoot { Name = "t", AbsPath = _root };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        return root.Id;
    }

    [Fact]
    public async Task New_image_gets_dimensions_thumbnail_and_job()
    {
        using (var img = new Image<Rgba32>(640, 480))
            await img.SaveAsPngAsync(Path.Combine(_root, "pic.png"));
        var rootId = await SeedRoot();

        ScanResult result;
        await using (var ctx = NewContext())
            result = await Scanner(ctx).ScanRootAsync(rootId);

        Assert.Equal(1, result.NewPhotos);
        Assert.Equal(1, result.ThumbsGenerated);
        Assert.Equal(1, result.JobsQueued);

        await using var verify = NewContext();
        var photo = await verify.Photos.SingleAsync();
        Assert.Equal(640, photo.Width);
        Assert.Equal(480, photo.Height);
        Assert.Equal("image/png", photo.Mime);
        Assert.Equal(1, await verify.TaggingJobs.CountAsync(j => j.PhotoId == photo.Id));

        var thumbPath = new ThumbnailService(new ThumbnailOptions { Dir = _thumbs }).PathFor(photo.FileHash);
        Assert.True(File.Exists(thumbPath));
    }

    [Fact]
    public async Task Bad_image_keeps_identity_but_no_thumb_or_job()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "broken.png"), "garbage");
        var rootId = await SeedRoot();

        ScanResult result;
        await using (var ctx = NewContext())
            result = await Scanner(ctx).ScanRootAsync(rootId);

        Assert.Equal(1, result.NewPhotos);        // 身分仍建立(有 bytes+hash)
        Assert.Equal(0, result.ThumbsGenerated);
        Assert.Equal(0, result.JobsQueued);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Photos.CountAsync());
        Assert.Null((await verify.Photos.SingleAsync()).Width);
        Assert.Equal(0, await verify.TaggingJobs.CountAsync());
    }
}
```

- [ ] **Step 5: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj
```

Expected: PASS。關鍵:既有 Scanner 測試**不因便利建構子而壞**;Enrich 2 綠。

- [ ] **Step 6: Commit**

```bash
cd /d/picture-management
git add src/Pm.Scanner src/Pm.Api tests/Pm.Scanner.Tests
git commit -m "feat: 掃描新身分補 EXIF/尺寸/縮圖 + 排 tagging_job(壞圖留身分不排)"
```

---

## Task 4: 對帳 —— 消失的位置標 missing(搬移/失蹤/復原)

走訪結束後,把這輪沒看到的位置標 `missing`(軟刪、保留 photo+tags)。同 hash 回來自動復原(走訪時 else 分支已把位置設回 present)。

**Files:**
- Modify: `src/Pm.Scanner/LibraryScanner.cs`(走訪後對帳)
- Create: `tests/Pm.Scanner.Tests/ReconcileTests.cs`

**Interfaces:**
- Consumes / Produces:同 Task 3(`ScanResult.MarkedMissing` 開始非零)。

- [ ] **Step 1: 在 ScanRootAsync 開頭記錄掃描起點**

在 `ScanRootAsync` 取得 `root` 之後、計數器宣告之前,加:

```csharp
        var scanStart = DateTimeOffset.UtcNow;
```

說明:走訪時每個「看到」的位置(新建/快路徑/換身分)都把 `last_seen_at` 設為 `UtcNow`(≥ scanStart);沒看到的維持舊值(< scanStart)。

- [ ] **Step 2: 走訪迴圈後加入對帳**

在 `foreach` 走訪迴圈**結束之後**、`return` 之前,加:

```csharp
        // 對帳:這輪沒看到、且仍標 present 的位置 → missing(軟刪,保留 photo+tags)。
        markedMissing = await db.PhotoLocations
            .Where(l => l.LibraryRootId == rootId
                        && l.Status == "present"
                        && l.LastSeenAt < scanStart)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, "missing"), ct);
```

- [ ] **Step 3: 寫失敗的對帳測試**

Create `tests/Pm.Scanner.Tests/ReconcileTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Scanner.Tests;

public class ReconcileTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-rec-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-recroot-{Guid.NewGuid():N}");
    private readonly string _thumbs = Path.Combine(Path.GetTempPath(), $"pm-recthumbs-{Guid.NewGuid():N}");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public ReconcileTests()
    {
        Directory.CreateDirectory(_root);
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        foreach (var d in new[] { _root, _thumbs }) if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    private LibraryScanner Scanner(PmDbContext ctx) =>
        new(ctx, new Sha256FileHasher(), new ExifImageMetadataReader(),
            new ThumbnailService(new ThumbnailOptions { Dir = _thumbs }));

    private async Task WriteImage(string rel, int seed)
    {
        var full = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var img = new Image<Rgba32>(16 + seed, 16);   // 不同尺寸 → 不同內容/hash
        await img.SaveAsPngAsync(full);
    }

    private async Task<long> SeedRoot()
    {
        await using var ctx = NewContext();
        var root = new LibraryRoot { Name = "t", AbsPath = _root };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();
        return root.Id;
    }

    [Fact]
    public async Task Deleted_file_marks_location_missing_but_keeps_photo()
    {
        await WriteImage("a.png", 1);
        await WriteImage("b.png", 2);
        var rootId = await SeedRoot();
        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);

        File.Delete(Path.Combine(_root, "a.png"));

        ScanResult result;
        await using (var ctx = NewContext()) result = await Scanner(ctx).ScanRootAsync(rootId);

        Assert.Equal(1, result.MarkedMissing);
        await using var verify = NewContext();
        Assert.Equal(2, await verify.Photos.CountAsync());   // photo 軟刪保留
        Assert.Equal(1, await verify.PhotoLocations.CountAsync(l => l.Status == "missing"));
        Assert.Equal(1, await verify.PhotoLocations.CountAsync(l => l.Status == "present"));
    }

    [Fact]
    public async Task Moved_file_keeps_one_present_location_identity_unchanged()
    {
        await WriteImage("old/a.png", 5);
        var rootId = await SeedRoot();
        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);

        File.Move(Path.Combine(_root, "old/a.png"), Path.Combine(_root, "new-a.png"));

        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Photos.CountAsync());   // 身分不變
        Assert.Equal(1, await verify.PhotoLocations.CountAsync(l => l.Status == "present"));
        Assert.Equal(1, await verify.PhotoLocations.CountAsync(l => l.Status == "missing"));
    }

    [Fact]
    public async Task Reappearing_file_is_auto_restored_to_present()
    {
        await WriteImage("a.png", 7);
        var rootId = await SeedRoot();
        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);

        var full = Path.Combine(_root, "a.png");
        var bytes = await File.ReadAllBytesAsync(full);
        File.Delete(full);
        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);   // → missing

        await File.WriteAllBytesAsync(full, bytes);   // 同內容回來
        await using (var ctx = NewContext()) await Scanner(ctx).ScanRootAsync(rootId);   // → present

        await using var verify = NewContext();
        Assert.Equal(1, await verify.PhotoLocations.CountAsync(l => l.Status == "present"));
        Assert.Equal(0, await verify.PhotoLocations.CountAsync(l => l.Status == "missing"));
    }
}
```

- [ ] **Step 4: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Scanner.Tests/Pm.Scanner.Tests.csproj --filter ReconcileTests
```

Expected: PASS,3 passed。

- [ ] **Step 5: Commit**

```bash
cd /d/picture-management
git add src/Pm.Scanner tests/Pm.Scanner.Tests
git commit -m "feat: 走訪後對帳(消失位置標 missing,搬移/失蹤/同 hash 復原)"
```

---

## Task 5: 待確認匣讀取端點

把「真失蹤」(整張圖無任何 present 位置)撈出來給前端 —— 搬移的(仍有 present)自然不在內。

**Files:**
- Modify: `src/Pm.Api/Program.cs`(加端點)
- Modify: `tests/Pm.Api.Tests/Pm.Api.Tests.csproj`(加 ImageSharp)
- Create: `tests/Pm.Api.Tests/ReconcileApiTests.cs`

**Interfaces:**
- Consumes: `PmDbContext`。
- Produces: `GET /api/reconcile/missing` → `200`,JSON 陣列 `[{ id, fileHash, paths: string[] }]`(僅含所有位置皆非 present 的 photo)。

- [ ] **Step 1: 加端點**

在 `src/Pm.Api/Program.cs`,健康/掃描端點附近加:

```csharp
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
```

(`using Microsoft.EntityFrameworkCore;` 地基已引入。)

- [ ] **Step 2: 測試專案加 ImageSharp**

Run:

```bash
cd /d/picture-management
dotnet add tests/Pm.Api.Tests/Pm.Api.Tests.csproj package SixLabors.ImageSharp
```

- [ ] **Step 3: 寫失敗的端點測試**

Create `tests/Pm.Api.Tests/ReconcileApiTests.cs`:

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

public class ReconcileApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-recapi-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-recapiroot-{Guid.NewGuid():N}");
    private readonly string _thumbs = Path.Combine(Path.GetTempPath(), $"pm-recapithumbs-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public ReconcileApiTests()
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

    [Fact]
    public async Task Missing_endpoint_lists_only_truly_gone_photos()
    {
        using (var img = new Image<Rgba32>(20, 20)) await img.SaveAsPngAsync(Path.Combine(_root, "gone.png"));
        using (var img = new Image<Rgba32>(30, 30)) await img.SaveAsPngAsync(Path.Combine(_root, "stay.png"));

        var client = _factory.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/roots", new { name = "t", absPath = _root }))
            .Content.ReadFromJsonAsync<RootCreated>();
        await client.PostAsync($"/api/roots/{created!.Id}/scan", null);

        File.Delete(Path.Combine(_root, "gone.png"));
        await client.PostAsync($"/api/roots/{created.Id}/scan", null);

        var resp = await client.GetAsync("/api/reconcile/missing");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();

        Assert.Contains("gone.png", body);
        Assert.DoesNotContain("stay.png", body);
    }
}
```

- [ ] **Step 4: 跑測試,確認綠燈**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj
```

Expected: PASS(健康 2 + 掃描 1 + 對帳 1 = 4)。

- [ ] **Step 5: 全 solution 總驗收**

Run:

```bash
cd /d/picture-management
dotnet test
```

Expected: 全綠。Scanner.Tests:Hasher 1 + Mtime 1 + Scanner 6 + Meta 3 + Thumb 2 + Enrich 2 + Reconcile 3 = 18;Data.Tests 6;Api.Tests 4;Ml.Tests 7。**合計 35 passed。**

- [ ] **Step 6: Commit**

```bash
cd /d/picture-management
git add src/Pm.Api tests/Pm.Api.Tests
git commit -m "feat: 待確認匣讀取端點(GET /api/reconcile/missing,只列真失蹤)"
```

---

## 完成定義(EXIF + 縮圖 + 對帳)

- 新身分首次出現:抽尺寸/MIME/EXIF(拍攝時間/相機/GPS)+ EXIF JSON、產 512px webp 縮圖(依 hash 分桶)、排 `tagging_job`。
- 壞圖/非圖:仍保留身分與位置,但無尺寸、無縮圖、不排 job(不阻斷整批)。
- 重掃對帳:消失的位置標 `missing`(軟刪,保留 photo+tags);搬移的圖仍有 present 位置、不進待確認匣;同 hash 回來自動復原。
- `GET /api/reconcile/missing` 只列「所有位置皆非 present」的真失蹤圖。
- `dotnet test` 全綠(35 passed)。

**明確不在本計畫:** WD14 實際推論寫 `photo_tag`(計畫 7);硬刪 purge 與待確認匣的使用者動作(繼續等待/移出/已刪除 → archived)(後續);縮圖串流端點(計畫 5)。

---

## Self-Review 註記

- **Spec 覆蓋:** 補齊 §5.1 對帳後半(missing 判斷)、§5.1 upsert 的「抽 EXIF/尺寸、產縮圖、塞 tagging_job」、§9「壞圖略過」「同 hash 多位置」、鐵則「軟刪保留」「縮圖放快取不碰原圖」。揪個人照所需的 `camera_model`/`gps`/`exif` 欄位於此填入。
- **既有測試不破:** 以便利建構子保留 `(db, hasher)` 呼叫;`ScanResult` 加欄為 append。
- **無 placeholder:** 每步皆可執行或含完整程式碼;修改既有檔處標明「將 X 改為 Y」。
- **型別一致:** `ImageMeta`、`ScanResult`(8 欄)、`IThumbnailService`、端點 JSON 形狀在 Interfaces 與測試中一致。
