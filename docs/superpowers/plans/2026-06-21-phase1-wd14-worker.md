# Phase 1 WD14 自動標籤 worker(ONNX in-proc)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **前置:** 需先完成地基(`IInferenceSessionFactory`)、Scanner(排 `tagging_job`)。
>
> **環境註(重要):** WD14 真推論需 **下載模型(HuggingFace)** 與 **DirectX12/GPU**(無則退 CPU,慢)。本計畫把**可測邏輯**(後處理、csv 解析、worker 的 DB 寫入)與 fake tagger 隔離測試;**真模型下載 + 實際推論**標 `[手動]`,需在使用者機器上跑。

**Goal:** 在 .NET 程序內實作 WD14 動漫自動標籤:背景服務輪詢 `tagging_job(state=pending)` → 讀原圖 → 前處理(448² 方形白底 BGR)→ ONNX 推論(經 `IInferenceSessionFactory`,預設 DirectML)→ 後處理(過門檻、category→kind)→ upsert `tag` + `photo_tag(source='wd14', confidence)` → job done;失敗 attempts++/state=error。

**Architecture:** `Pm.Ml` 加:`Wd14Postprocess`(純函式:probs+tags+門檻→選中)、`Wd14Tags`(csv 解析)、`Wd14Preprocess`(ImageSharp→tensor)、`Wd14ModelProvider`(缺檔則下載)、`IWd14Tagger`+`Wd14Tagger`(串起前處理/推論/後處理)。`Pm.Api` 加 `TaggingWorker`(`BackgroundService`,可測的 `ProcessNextAsync`)+ DI。

**Tech Stack:** .NET 10、`Microsoft.ML.OnnxRuntime.DirectML`、`SixLabors.ImageSharp`、EF Core SQLite、xUnit。

## Global Constraints

- **ML 推論在 .NET 程序內(in-proc),經 `IInferenceSessionFactory`**;不另開程序、不綁 CUDA(預設 DirectML,無 GPU 退 CPU)。
- **tag 來源**:WD14 寫 `photo_tag.source='wd14'` + `confidence`,**不與 manual/path 混**。
- **`tagging_job` 程序內佇列**:輪詢 pending → running → done/error(attempts++ 可重試)。
- **不改原圖**:只讀圖做推論。
- 模型:SmilingWolf `wd-vit-tagger-v3`(HF ONNX);category→kind 與門檻**待對實際 `selected_tags.csv` 校正**(spec §12)。

---

## File Structure

```
src/
├─ Pm.Ml/
│  ├─ Pm.Ml.csproj                 # +SixLabors.ImageSharp
│  ├─ Wd14Tag.cs                   # record(Id,Name,Category)
│  ├─ Wd14Tags.cs                  # selected_tags.csv 解析
│  ├─ Wd14Postprocess.cs           # 純函式:probs→選中(門檻/kind)
│  ├─ Wd14Preprocess.cs            # ImageSharp → DenseTensor
│  ├─ Wd14Options.cs
│  ├─ Wd14ModelProvider.cs         # 缺檔下載
│  ├─ IWd14Tagger.cs
│  └─ Wd14Tagger.cs                # 串起前處理/推論/後處理
└─ Pm.Api/
   ├─ TaggingWorker.cs             # BackgroundService + ProcessNextAsync
   └─ Program.cs                   # +DI +AddHostedService
tests/
├─ Pm.Ml.Tests/
│  ├─ Wd14PostprocessTests.cs
│  └─ Wd14TagsTests.cs
└─ Pm.Api.Tests/
   └─ TaggingWorkerTests.cs        # fake tagger
```

---

## Task 1: 後處理 + csv 解析(純函式,完整測試)

WD14 的可測核心:把模型輸出機率配上 tag 表、過門檻、分 kind。

**Files:**
- Create: `src/Pm.Ml/Wd14Tag.cs`、`Wd14Tags.cs`、`Wd14Postprocess.cs`
- Create: `tests/Pm.Ml.Tests/Wd14TagsTests.cs`、`Wd14PostprocessTests.cs`

**Interfaces:**
- Produces:
  - `record Wd14Tag(long Id, string Name, int Category)`
  - `Wd14Tags.Parse(IEnumerable<string> csvLines) -> IReadOnlyList<Wd14Tag>`
  - `Wd14Postprocess.KindOf(int category) -> string`
  - `Wd14Postprocess.Select(IReadOnlyList<float> probs, IReadOnlyList<Wd14Tag> tags, float generalThreshold, float characterThreshold) -> IReadOnlyList<(string Name, string Kind, float Conf)>`

- [ ] **Step 1: 寫 record / csv 解析 / 後處理**

Create `src/Pm.Ml/Wd14Tag.cs`:

```csharp
namespace Pm.Ml;

public sealed record Wd14Tag(long Id, string Name, int Category);
```

Create `src/Pm.Ml/Wd14Tags.cs`:

```csharp
namespace Pm.Ml;

// selected_tags.csv 欄位:tag_id,name,category,count(首列為標頭)
public static class Wd14Tags
{
    public static IReadOnlyList<Wd14Tag> Parse(IEnumerable<string> csvLines)
    {
        var list = new List<Wd14Tag>();
        var first = true;
        foreach (var line in csvLines)
        {
            if (first) { first = false; continue; }          // 跳標頭
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(',');
            if (cols.Length < 3) continue;
            var id = long.TryParse(cols[0], out var x) ? x : 0;
            var name = cols[1];
            var cat = int.TryParse(cols[2], out var c) ? c : 0;
            list.Add(new Wd14Tag(id, name, cat));
        }
        return list;
    }
}
```

Create `src/Pm.Ml/Wd14Postprocess.cs`:

```csharp
namespace Pm.Ml;

public static class Wd14Postprocess
{
    // danbooru/WD14 category → 我們的 tag.kind
    // 0=general, 3=copyright, 4=character, 9=rating(meta);其餘歸 general。
    // 註:實際 category 值以 selected_tags.csv 為準,必要時校正。
    public static string KindOf(int category) => category switch
    {
        4 => "character",
        3 => "copyright",
        9 => "meta",
        0 => "general",
        _ => "general",
    };

    public static IReadOnlyList<(string Name, string Kind, float Conf)> Select(
        IReadOnlyList<float> probs,
        IReadOnlyList<Wd14Tag> tags,
        float generalThreshold,
        float characterThreshold)
    {
        var result = new List<(string, string, float)>();
        var n = Math.Min(probs.Count, tags.Count);
        for (var i = 0; i < n; i++)
        {
            var t = tags[i];
            if (t.Category == 9) continue;                    // rating 不當一般標籤
            var thr = t.Category == 4 ? characterThreshold : generalThreshold;
            if (probs[i] >= thr)
                result.Add((t.Name, KindOf(t.Category), probs[i]));
        }
        return result;
    }
}
```

- [ ] **Step 2: 寫失敗的測試**

Create `tests/Pm.Ml.Tests/Wd14TagsTests.cs`:

```csharp
using Pm.Ml;
using Xunit;

namespace Pm.Ml.Tests;

public class Wd14TagsTests
{
    [Fact]
    public void Parses_csv_skipping_header()
    {
        var lines = new[]
        {
            "tag_id,name,category,count",
            "1,1girl,0,1000000",
            "2,hakurei_reimu,4,50000",
            "3,general,9,999",
        };
        var tags = Wd14Tags.Parse(lines);
        Assert.Equal(3, tags.Count);
        Assert.Equal("1girl", tags[0].Name);
        Assert.Equal(0, tags[0].Category);
        Assert.Equal(4, tags[1].Category);
    }
}
```

Create `tests/Pm.Ml.Tests/Wd14PostprocessTests.cs`:

```csharp
using Pm.Ml;
using Xunit;

namespace Pm.Ml.Tests;

public class Wd14PostprocessTests
{
    [Theory]
    [InlineData(0, "general")]
    [InlineData(4, "character")]
    [InlineData(3, "copyright")]
    [InlineData(9, "meta")]
    [InlineData(7, "general")]
    public void KindOf_maps_category(int cat, string kind) => Assert.Equal(kind, Wd14Postprocess.KindOf(cat));

    [Fact]
    public void Select_applies_per_category_thresholds_and_skips_rating()
    {
        var tags = new List<Wd14Tag>
        {
            new(1, "1girl", 0),            // general
            new(2, "reimu", 4),            // character
            new(3, "low_conf_char", 4),    // character,信心不足
            new(4, "explicit", 9),         // rating → 跳過
        };
        var probs = new[] { 0.9f, 0.9f, 0.5f, 0.99f };

        var selected = Wd14Postprocess.Select(probs, tags, generalThreshold: 0.35f, characterThreshold: 0.85f);

        Assert.Contains(selected, s => s.Name == "1girl" && s.Kind == "general");
        Assert.Contains(selected, s => s.Name == "reimu" && s.Kind == "character");
        Assert.DoesNotContain(selected, s => s.Name == "low_conf_char");   // 0.5 < 0.85
        Assert.DoesNotContain(selected, s => s.Name == "explicit");        // rating 跳過
    }
}
```

- [ ] **Step 3: 跑測試 + Commit**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Ml.Tests/Pm.Ml.Tests.csproj --filter "Wd14TagsTests|Wd14PostprocessTests"
git add src/Pm.Ml tests/Pm.Ml.Tests
git commit -m "feat: WD14 後處理(門檻/category→kind)+ selected_tags.csv 解析(純函式)"
```

Expected: PASS。

---

## Task 2: 前處理 + 模型供應 + Wd14Tagger(真推論 [手動])

ImageSharp 把圖轉成 WD14 需要的張量;缺模型則從 HF 下載;串起前處理→推論→後處理。**實際推論需模型 + GPU,標 [手動]。**

**Files:**
- Modify: `src/Pm.Ml/Pm.Ml.csproj`(+ImageSharp)
- Create: `src/Pm.Ml/Wd14Preprocess.cs`、`Wd14Options.cs`、`Wd14ModelProvider.cs`、`IWd14Tagger.cs`、`Wd14Tagger.cs`

**Interfaces:**
- Consumes: `IInferenceSessionFactory`(地基)。
- Produces:
  - `Wd14Options { string ModelDir; string ModelOnnxUrl; string TagsCsvUrl; float GeneralThreshold = 0.35f; float CharacterThreshold = 0.85f; int Size = 448; }`
  - `Wd14Preprocess.ToTensor(string absPath, int size = 448) -> DenseTensor<float>`(NHWC [1,size,size,3],BGR,0–255)
  - `IWd14Tagger { Task<IReadOnlyList<(string Name, string Kind, float Conf)>> TagAsync(string imageAbsPath, CancellationToken ct = default); }`
  - `Wd14Tagger(IInferenceSessionFactory factory, Wd14Options options)` 實作。

- [ ] **Step 1: 裝 ImageSharp 進 Pm.Ml**

Run:

```bash
cd /d/picture-management
dotnet add src/Pm.Ml/Pm.Ml.csproj package SixLabors.ImageSharp
```

- [ ] **Step 2: 前處理**

Create `src/Pm.Ml/Wd14Preprocess.cs`:

```csharp
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Pm.Ml;

public static class Wd14Preprocess
{
    // WD14:方形白底 padding → resize size² → BGR、0–255 float、NHWC。
    public static DenseTensor<float> ToTensor(string absPath, int size = 448)
    {
        using var img = Image.Load<Rgb24>(absPath);

        var side = Math.Max(img.Width, img.Height);
        using var canvas = new Image<Rgb24>(side, side, new Rgb24(255, 255, 255));
        var ox = (side - img.Width) / 2;
        var oy = (side - img.Height) / 2;

        img.ProcessPixelRows(canvas, (src, dst) =>
        {
            for (var y = 0; y < img.Height; y++)
            {
                var s = src.GetRowSpan(y);
                var d = dst.GetRowSpan(oy + y);
                for (var x = 0; x < img.Width; x++) d[ox + x] = s[x];
            }
        });

        canvas.Mutate(c => c.Resize(size, size));

        var tensor = new DenseTensor<float>(new[] { 1, size, size, 3 });
        canvas.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < size; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < size; x++)
                {
                    var p = row[x];
                    tensor[0, y, x, 0] = p.B;   // BGR
                    tensor[0, y, x, 1] = p.G;
                    tensor[0, y, x, 2] = p.R;
                }
            }
        });
        return tensor;
    }
}
```

- [ ] **Step 3: options + 模型供應 + tagger**

Create `src/Pm.Ml/Wd14Options.cs`:

```csharp
namespace Pm.Ml;

public sealed class Wd14Options
{
    public string ModelDir { get; set; } = "models/wd14";
    public string ModelOnnxUrl { get; set; } =
        "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/model.onnx";
    public string TagsCsvUrl { get; set; } =
        "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/selected_tags.csv";
    public float GeneralThreshold { get; set; } = 0.35f;
    public float CharacterThreshold { get; set; } = 0.85f;
    public int Size { get; set; } = 448;
}
```

Create `src/Pm.Ml/Wd14ModelProvider.cs`:

```csharp
namespace Pm.Ml;

public static class Wd14ModelProvider
{
    // 缺檔則從 HF 下載;回 (modelPath, tagsCsvPath)。
    public static async Task<(string Model, string Tags)> EnsureAsync(Wd14Options opt, CancellationToken ct = default)
    {
        Directory.CreateDirectory(opt.ModelDir);
        var model = Path.Combine(opt.ModelDir, "model.onnx");
        var tags = Path.Combine(opt.ModelDir, "selected_tags.csv");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (!File.Exists(model)) await DownloadAsync(http, opt.ModelOnnxUrl, model, ct);
        if (!File.Exists(tags)) await DownloadAsync(http, opt.TagsCsvUrl, tags, ct);
        return (model, tags);
    }

    private static async Task DownloadAsync(HttpClient http, string url, string dest, CancellationToken ct)
    {
        await using var s = await http.GetStreamAsync(url, ct);
        await using var f = File.Create(dest);
        await s.CopyToAsync(f, ct);
    }
}
```

Create `src/Pm.Ml/IWd14Tagger.cs`:

```csharp
namespace Pm.Ml;

public interface IWd14Tagger
{
    Task<IReadOnlyList<(string Name, string Kind, float Conf)>> TagAsync(
        string imageAbsPath, CancellationToken ct = default);
}
```

Create `src/Pm.Ml/Wd14Tagger.cs`:

```csharp
using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

public sealed class Wd14Tagger(IInferenceSessionFactory factory, Wd14Options options) : IWd14Tagger
{
    private InferenceSession? _session;
    private IReadOnlyList<Wd14Tag>? _tags;
    private string? _inputName;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_session is not null) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_session is not null) return;
            var (modelPath, tagsPath) = await Wd14ModelProvider.EnsureAsync(options, ct);
            var session = factory.Create(modelPath);
            _inputName = session.InputMetadata.Keys.First();
            _tags = Wd14Tags.Parse(await File.ReadAllLinesAsync(tagsPath, ct));
            _session = session;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<(string Name, string Kind, float Conf)>> TagAsync(
        string imageAbsPath, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        var tensor = Wd14Preprocess.ToTensor(imageAbsPath, options.Size);
        using var results = _session!.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName!, tensor) });
        var probs = results.First().AsEnumerable<float>().ToArray();
        return Wd14Postprocess.Select(probs, _tags!, options.GeneralThreshold, options.CharacterThreshold);
    }
}
```

- [ ] **Step 4: build 驗證**

Run:

```bash
cd /d/picture-management
dotnet build src/Pm.Ml/Pm.Ml.csproj
```

Expected: 編譯成功(本 task 不跑真推論)。

- [ ] **Step 5: [手動] 真模型煙霧測試(使用者機器,需網路 + GPU)**

> 此步驟下載 ~300MB 模型並做一次真推論,自動化環境多半無 GPU/受限網路,故交由使用者執行。寫一個臨時 console 或用既有測試專案手動跑:
> 用 `new Wd14Tagger(new DirectMlSessionFactory(), new Wd14Options())` 對一張真動漫圖呼叫 `TagAsync`,印出標籤;確認有合理結果(如 `1girl`、角色名)。模型首次會下載到 `models/wd14`。
> 若 category 值或門檻看起來不對,對照下載下來的 `selected_tags.csv` 調整 `Wd14Postprocess.KindOf` 與 `Wd14Options` 門檻(spec §12 已預期此校正)。

- [ ] **Step 6: Commit**

```bash
cd /d/picture-management
git add src/Pm.Ml
git commit -m "feat: WD14 前處理(448² BGR)+ 模型供應(HF 下載)+ Wd14Tagger(ONNX in-proc)"
```

---

## Task 3: `TaggingWorker`(輪詢 + DB 寫入,fake tagger 測試)

背景服務的 DB 邏輯:取 pending job → 解析圖路徑 → 呼叫 tagger → upsert tag/photo_tag → 標 done/error。以 fake `IWd14Tagger` 測試,**不碰真模型**。

**Files:**
- Create: `src/Pm.Api/TaggingWorker.cs`
- Create: `tests/Pm.Api.Tests/TaggingWorkerTests.cs`

**Interfaces:**
- Consumes: `IServiceScopeFactory`、`IWd14Tagger`、`PmDbContext`。
- Produces:`TaggingWorker`(`BackgroundService`),含可測方法 `Task<bool> ProcessNextAsync(PmDbContext db, CancellationToken ct)`(處理一筆回 true,沒 pending 回 false)。

- [ ] **Step 1: 寫 worker**

Create `src/Pm.Api/TaggingWorker.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Ml;

namespace Pm.Api;

public sealed class TaggingWorker(
    IServiceScopeFactory scopes, IWd14Tagger tagger, ILogger<TaggingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
            var processed = await ProcessNextAsync(db, ct);
            if (!processed) await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    public async Task<bool> ProcessNextAsync(PmDbContext db, CancellationToken ct)
    {
        var job = await db.TaggingJobs
            .Where(j => j.State == "pending")
            .OrderBy(j => j.EnqueuedAt)
            .FirstOrDefaultAsync(ct);
        if (job is null) return false;

        job.State = "running";
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            var path = await ResolvePathAsync(db, job.PhotoId, ct)
                       ?? throw new FileNotFoundException($"photo {job.PhotoId} 無可用位置");

            foreach (var (name, kind, conf) in await tagger.TagAsync(path, ct))
            {
                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
                if (tag is null)
                {
                    tag = new Tag { Name = name, Kind = kind };
                    db.Tags.Add(tag);
                    await db.SaveChangesAsync(ct);
                }
                if (!await db.PhotoTags.AnyAsync(pt => pt.PhotoId == job.PhotoId && pt.TagId == tag.Id, ct))
                    db.PhotoTags.Add(new PhotoTag { PhotoId = job.PhotoId, TagId = tag.Id, Source = "wd14", Confidence = conf });
            }

            job.State = "done";
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            job.Attempts++;
            job.State = "error";
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            log.LogWarning(ex, "tagging job {PhotoId} 失敗", job.PhotoId);
            return true;
        }
    }

    private static async Task<string?> ResolvePathAsync(PmDbContext db, long photoId, CancellationToken ct)
    {
        var loc = await db.PhotoLocations
            .Include(l => l.LibraryRoot)
            .Where(l => l.PhotoId == photoId && l.Status == "present")
            .FirstOrDefaultAsync(ct);
        return loc is null ? null : Path.Combine(loc.LibraryRoot.AbsPath, loc.RelPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
```

- [ ] **Step 2: 寫失敗的測試(fake tagger)**

Create `tests/Pm.Api.Tests/TaggingWorkerTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pm.Api;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Ml;
using Xunit;

namespace Pm.Api.Tests;

public class TaggingWorkerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-worker-{Guid.NewGuid():N}.sqlite");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pm-workerroot-{Guid.NewGuid():N}");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public TaggingWorkerTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "a.png"), "x");   // 路徑存在即可(fake 不真讀)
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

    private sealed class FakeTagger : IWd14Tagger
    {
        public Task<IReadOnlyList<(string Name, string Kind, float Conf)>> TagAsync(string p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(string, string, float)>>(new[]
            {
                ("1girl", "general", 0.95f),
                ("hakurei_reimu", "character", 0.91f),
            });
    }

    private sealed class ThrowingTagger : IWd14Tagger
    {
        public Task<IReadOnlyList<(string Name, string Kind, float Conf)>> TagAsync(string p, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    private async Task<long> SeedPendingJob()
    {
        await using var ctx = NewContext();
        var root = new LibraryRoot { Name = "t", AbsPath = _root };
        var photo = new Photo { FileHash = new string('a', 64), FileSize = 1 };
        photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = "a.png", Status = "present" });
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
        ctx.TaggingJobs.Add(new TaggingJob { PhotoId = photo.Id });
        await ctx.SaveChangesAsync();
        return photo.Id;
    }

    private TaggingWorker Worker(IWd14Tagger tagger) =>
        new(new DummyScopeFactory(), tagger, NullLogger<TaggingWorker>.Instance);

    // ProcessNextAsync 直接收 db,不用 scope factory;給個不會被呼叫的 dummy。
    private sealed class DummyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException();
    }

    [Fact]
    public async Task Processes_pending_job_writes_tags_and_marks_done()
    {
        var photoId = await SeedPendingJob();

        await using var ctx = NewContext();
        var ok = await Worker(new FakeTagger()).ProcessNextAsync(ctx, default);
        Assert.True(ok);

        await using var verify = NewContext();
        Assert.Equal("done", (await verify.TaggingJobs.SingleAsync()).State);
        Assert.Equal(2, await verify.PhotoTags.CountAsync(pt => pt.PhotoId == photoId && pt.Source == "wd14"));
        var charTag = await verify.Tags.SingleAsync(t => t.Name == "hakurei_reimu");
        Assert.Equal("character", charTag.Kind);
    }

    [Fact]
    public async Task No_pending_returns_false()
    {
        await using var ctx = NewContext();
        Assert.False(await Worker(new FakeTagger()).ProcessNextAsync(ctx, default));
    }

    [Fact]
    public async Task Failure_marks_error_and_increments_attempts()
    {
        await SeedPendingJob();

        await using var ctx = NewContext();
        await Worker(new ThrowingTagger()).ProcessNextAsync(ctx, default);

        await using var verify = NewContext();
        var job = await verify.TaggingJobs.SingleAsync();
        Assert.Equal("error", job.State);
        Assert.Equal(1, job.Attempts);
    }
}
```

- [ ] **Step 3: 跑測試 + Commit**

Run:

```bash
cd /d/picture-management
dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter TaggingWorkerTests
git add src/Pm.Api tests/Pm.Api.Tests
git commit -m "feat: TaggingWorker(輪詢 tagging_job→寫 photo_tag(wd14)→done/error,fake tagger 測試)"
```

Expected: PASS,3 passed。

---

## Task 4: 接上 host(DI + AddHostedService)+ 端到端 [手動]

**Files:**
- Modify: `src/Pm.Api/Program.cs`
- Modify: `src/Pm.Api/appsettings.json`(Wd14 區段)

**Interfaces:**
- Produces:啟動時註冊 `Wd14Options`、`IInferenceSessionFactory`、`IWd14Tagger`、`TaggingWorker` 並 `AddHostedService`。

- [ ] **Step 1: DI + hosted service**

在 `src/Pm.Api/Program.cs` 服務註冊區加:

```csharp
var wd14 = builder.Configuration.GetSection("Wd14").Get<Wd14Options>() ?? new Wd14Options();
builder.Services.AddSingleton(wd14);

// EP:啟動參數/偵測選 backend(地基 IInferenceSessionFactory)。
var backend = InferenceBackendSelector.Select(
    builder.Configuration["Inference:Backend"], gpuVendor: null);   // 簡化:預設 CPU/可由設定指定 dml
builder.Services.AddSingleton<IInferenceSessionFactory>(_ =>
    backend == InferenceBackend.DirectMl ? new DirectMlSessionFactory() : new CpuSessionFactory());

builder.Services.AddSingleton<IWd14Tagger, Wd14Tagger>();
builder.Services.AddHostedService<TaggingWorker>();
```

> 註:`Wd14Tagger` 用 singleton(模型載一次)。`gpuVendor` 偵測可後續接 Windows `Win32_VideoController`;先讓 `Inference:Backend` 設定可指定 `dml`/`cpu`。

並在 `src/Pm.Api/appsettings.json` 加(與 `ConnectionStrings` 同層):

```json
  "Inference": { "Backend": "dml" },
  "Wd14": {
    "ModelDir": "models/wd14",
    "GeneralThreshold": 0.35,
    "CharacterThreshold": 0.85
  },
```

- [ ] **Step 2: 全 solution 編譯 + 既有測試**

Run:

```bash
cd /d/picture-management
dotnet build
dotnet test
```

Expected: 編譯成功;所有單元/整合測試綠(worker 用 fake,不需模型)。

- [ ] **Step 3: [手動] 端到端(使用者機器)**

> 1. `dotnet run --project src/Pm.Api`(首次會背景下載 WD14 模型到 `models/wd14`)。
> 2. 用前端或 API 建 root 指向一個有動漫圖的資料夾、觸發掃描(產生 `tagging_job`)。
> 3. 等 worker 跑完(看 log),`GET /api/photos/{id}` 應出現 `source:"wd14"` 的分色標籤(角色綠/一般藍…)。
> 4. 在檢視器確認 WD14 建議以虛線 + 信心 % 呈現。
> 5. 若顯卡是 NVIDIA 想跑滿速,改 `Inference:Backend` 或加 CUDA publish profile(spec §7)。

- [ ] **Step 4: Commit**

```bash
cd /d/picture-management
git add src/Pm.Api
git commit -m "feat: WD14 worker 接上 host(DI + AddHostedService + EP 選擇)"
```

---

## 完成定義(WD14 worker)

- 後處理/csv/worker DB 邏輯**單元+整合測試全綠**(fake tagger,不需模型)。
- `Wd14Tagger` 走 `IInferenceSessionFactory`(預設 DirectML、可退 CPU),模型缺則自 HF 下載。
- worker 輪詢 `tagging_job`:pending→running→done;失敗→error/attempts++。
- 標籤寫 `photo_tag(source='wd14', confidence)`,tag.kind 依 category。
- **[手動]** 端到端真推論在使用者機器驗證(GPU/網路);category/門檻對 `selected_tags.csv` 校正。

**明確不在本計畫:** WD14 建議的接受/拒絕寫回(UI 互動,可在計畫 6 之上增強)、CLIP/語意搜尋(Phase 2)。

---

## Self-Review 註記

- **Spec 覆蓋:** §5.2 標籤流程(輪詢→推論→過門檻→upsert)、§3 ML in-proc 經 `IInferenceSessionFactory`、§2 WD14 模型、鐵則「source=wd14+confidence」「不綁 CUDA」「DB-as-queue 程序內」。
- **可測 / 不可測切分:** 純邏輯(後處理/csv/worker DB)完整測試;真模型/GPU 推論隔離為 `[手動]`,full-auto 跑到此交棒使用者機器。
- **校正點:** category→kind 與門檻明示「對 selected_tags.csv 校正」(對齊 spec §12)。
```
