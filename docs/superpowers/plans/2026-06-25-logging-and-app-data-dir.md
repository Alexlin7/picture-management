# 檔案 Logging + App Data Dir 收斂 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 引入 Serilog rolling file logging,並把 SQLite / 縮圖 / WD14 模型 / log 落點收斂到單一 app data dir(開發維持相對、打包 exe 用 `%LOCALAPPDATA%`),使單檔 exe 雙擊執行時資料不散落、log 不消失。

**Architecture:** 新增 `StoragePaths` 在 `Program.cs` 建立 service 前算好 BaseDir 與各子路徑,把解析後的**絕對路徑寫回 `builder.Configuration`** 的既有 key(`ConnectionStrings:Pm` / `Thumbnails:Dir` / `Inference:Wd14:ModelDir`),既有 wiring 程式碼不動就吃到絕對路徑。Serilog 以 `UseSerilog` 程式化接線,console + rolling file 雙 sink,MinimumLevel 自 config 讀取以避開「`UseSerilog` 繞過 MS `Logging:LogLevel`」的坑。

**Tech Stack:** .NET 10 / ASP.NET Core(Minimal API)、Serilog.AspNetCore(內含 Console + File sink)、xUnit、EF Core + SQLite。

## Global Constraints

- TargetFramework `net10.0`;`Nullable` enable;`ImplicitUsings` enable。
- 後端 TDD;測試 DB 隔離(每測試獨立 SQLite 檔或 `:memory:`)。
- 單一 .NET 程序、API 只 bind localhost、無認證(鐵則 #8)。
- **落點扁平命名**:BaseDir 底下沿用 `pm.sqlite` / `thumbs` / `models/wd14`,新增 `logs/`。不改成 `data/` 子夾(避免 dev 資料搬遷)。
- **BaseDir 解析優先序**:① config `Storage:BaseDir`(含環境變數 `Storage__BaseDir`)有設則用之;② 否則 `env.IsProduction()` → `%LOCALAPPDATA%\sus-picture-management\`;③ 否則(Development / 測試 / 其它)→ `Directory.GetCurrentDirectory()`。
- **子路徑相對 vs 絕對**:config 給相對值 → 以 BaseDir 為根組合;config 給絕對值 → 原樣保留(`Path.IsPathRooted` 判斷)。
- Serilog file sink:按日 rolling、`retainedFileCountLimit: 14`、`fileSizeLimitBytes: 50MB` + `rollOnFileSizeLimit: true`。
- MinimumLevel 讀 `Logging:LogLevel:Default`(解析失敗預設 `Information`)+ `Microsoft.AspNetCore` Override 為 `Warning`。
- `PmDbContextFactory`(設計期 migration 工具)硬編 `Data Source=pm.sqlite` **不動**。

---

### Task 1: `StoragePaths` 路徑解析器

**Files:**
- Create: `src/Pm.Api/StoragePaths.cs`
- Test: `tests/Pm.Api.Tests/StoragePathsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class StoragePaths`
  - `public string BaseDir { get; }`
  - `public string SqliteDataSource { get; }`(完整連線字串值,形如 `Data Source=<絕對路徑>`)
  - `public string ThumbsDir { get; }`(絕對)
  - `public string ModelDir { get; }`(絕對)
  - `public string LogDir { get; }`(絕對)
  - `public static StoragePaths Resolve(IHostEnvironment env, IConfiguration config)`

- [ ] **Step 1: 寫失敗測試**

`tests/Pm.Api.Tests/StoragePathsTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Pm.Api;
using Xunit;

namespace Pm.Api.Tests;

public class StoragePathsTests
{
    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Pm.Api.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    [Fact]
    public void Development_uses_current_directory_as_base()
    {
        var env = new FakeEnv { EnvironmentName = "Development" };
        var p = StoragePaths.Resolve(env, Config());

        Assert.Equal(Directory.GetCurrentDirectory(), p.BaseDir);
        Assert.Equal($"Data Source={Path.Combine(p.BaseDir, "pm.sqlite")}", p.SqliteDataSource);
        Assert.Equal(Path.Combine(p.BaseDir, "thumbs"), p.ThumbsDir);
        Assert.Equal(Path.Combine(p.BaseDir, "models", "wd14"), p.ModelDir);
        Assert.Equal(Path.Combine(p.BaseDir, "logs"), p.LogDir);
    }

    [Fact]
    public void Production_uses_localappdata_subfolder_as_base()
    {
        var env = new FakeEnv { EnvironmentName = "Production" };
        var p = StoragePaths.Resolve(env, Config());

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "sus-picture-management");
        Assert.Equal(expected, p.BaseDir);
    }

    [Fact]
    public void Explicit_base_dir_override_wins_over_environment()
    {
        var env = new FakeEnv { EnvironmentName = "Production" };
        var dir = Path.Combine(Path.GetTempPath(), "pm-override-test");
        var p = StoragePaths.Resolve(env, Config(("Storage:BaseDir", dir)));

        Assert.Equal(Path.GetFullPath(dir), p.BaseDir);
        Assert.Equal(Path.Combine(Path.GetFullPath(dir), "logs"), p.LogDir);
    }

    [Fact]
    public void Absolute_subpath_in_config_is_not_reprefixed()
    {
        var env = new FakeEnv { EnvironmentName = "Production" };
        var absThumbs = Path.Combine(Path.GetTempPath(), "external-thumbs");
        var p = StoragePaths.Resolve(env, Config(("Thumbnails:Dir", absThumbs)));

        Assert.Equal(absThumbs, p.ThumbsDir);
    }

    [Fact]
    public void Relative_subpath_in_config_is_combined_with_base()
    {
        var env = new FakeEnv { EnvironmentName = "Development" };
        var p = StoragePaths.Resolve(env, Config(("Thumbnails:Dir", "custom-thumbs")));

        Assert.Equal(Path.Combine(p.BaseDir, "custom-thumbs"), p.ThumbsDir);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter "FullyQualifiedName~StoragePathsTests"`
Expected: 編譯失敗 —— `StoragePaths` 型別不存在(`CS0246`)。

- [ ] **Step 3: 寫最小實作**

`src/Pm.Api/StoragePaths.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace Pm.Api;

// 執行期落點集中解析點。BaseDir 規則見 plan Global Constraints。
// 子路徑:config 給相對 → 以 BaseDir 為根;給絕對 → 原樣保留。
public sealed class StoragePaths
{
    public string BaseDir { get; }
    public string SqliteDataSource { get; }
    public string ThumbsDir { get; }
    public string ModelDir { get; }
    public string LogDir { get; }

    private StoragePaths(string baseDir, string sqlite, string thumbs, string modelDir, string logDir)
    {
        BaseDir = baseDir;
        SqliteDataSource = sqlite;
        ThumbsDir = thumbs;
        ModelDir = modelDir;
        LogDir = logDir;
    }

    public static StoragePaths Resolve(IHostEnvironment env, IConfiguration config)
    {
        var baseDir = ResolveBaseDir(env, config);

        var sqliteFile = SqliteFileName(config.GetConnectionString("Pm"));     // 預設 "pm.sqlite"
        var thumbs = config["Thumbnails:Dir"] ?? "thumbs";
        var model = config["Inference:Wd14:ModelDir"] ?? "models/wd14";

        return new StoragePaths(
            baseDir,
            $"Data Source={Combine(baseDir, sqliteFile)}",
            Combine(baseDir, thumbs),
            Combine(baseDir, model),
            Combine(baseDir, "logs"));
    }

    private static string ResolveBaseDir(IHostEnvironment env, IConfiguration config)
    {
        var overridden = config["Storage:BaseDir"];
        if (!string.IsNullOrWhiteSpace(overridden)) return Path.GetFullPath(overridden);
        if (env.IsProduction())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "sus-picture-management");
        return Directory.GetCurrentDirectory();
    }

    // 相對 → 接在 BaseDir 後並正規化;絕對 → 原樣。
    private static string Combine(string baseDir, string maybeRelative)
        => Path.IsPathRooted(maybeRelative)
            ? maybeRelative
            : Path.Combine(baseDir, maybeRelative);

    // 從連線字串拆出 DataSource 檔名(預設 pm.sqlite)。
    private static string SqliteFileName(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "pm.sqlite";
        var ds = new SqliteConnectionStringBuilder(connectionString).DataSource;
        return string.IsNullOrWhiteSpace(ds) ? "pm.sqlite" : ds;
    }
}
```

> 註:`models/wd14` 經 `Path.Combine` 後在 Windows 變 `models\wd14`,測試以 `Path.Combine(p.BaseDir, "models", "wd14")` 比對,跨平台一致。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Pm.Api.Tests/Pm.Api.Tests.csproj --filter "FullyQualifiedName~StoragePathsTests"`
Expected: PASS(5 passed)。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Api/StoragePaths.cs tests/Pm.Api.Tests/StoragePathsTests.cs
git commit -m "feat(api): StoragePaths 集中解析執行期落點(dev 相對 / prod appdata)"
```

---

### Task 2: 接入 Program.cs(落點寫回 config)+ 測試隔離

**Files:**
- Modify: `src/Pm.Api/Program.cs:9`(緊接 `var builder = WebApplication.CreateBuilder(args);` 之後插入解析 + 寫回)
- Create: `tests/Pm.Api.Tests/TestStorageBootstrap.cs`

**Interfaces:**
- Consumes: `StoragePaths.Resolve(IHostEnvironment, IConfiguration)`(Task 1)
- Produces: 解析後的 `paths` 區域變數(供 Task 3 的 Serilog 用 `paths.LogDir`)。

- [ ] **Step 1: 加測試隔離 bootstrap(避免整合測試污染真實 appdata)**

整合測試以 `WebApplicationFactory<Program>` 跑真正的 `Program.cs`,其預設環境非 Development;若不隔離,BaseDir 會落到真實 `%LOCALAPPDATA%`。用 module initializer 在整個測試程序啟動前設 `Storage__BaseDir` 到 temp(`Storage:BaseDir` 覆寫勝出 → 所有測試的 db/thumbs/models/logs 都落 temp),零改既有測試。

`tests/Pm.Api.Tests/TestStorageBootstrap.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace Pm.Api.Tests;

// 整個測試程序啟動前執行一次:把執行期落點導到隔離 temp 目錄,
// 避免 WebApplicationFactory 跑 Program.cs 時污染真實 %LOCALAPPDATA%。
internal static class TestStorageBootstrap
{
    [ModuleInitializer]
    public static void Init()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pm-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("Storage__BaseDir", dir);
    }
}
```

- [ ] **Step 2: 在 Program.cs 解析並寫回絕對路徑**

`src/Pm.Api/Program.cs`,把目前的:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new SqliteBusyTimeoutInterceptor(TimeSpan.FromSeconds(5)));
```

改成:

```csharp
var builder = WebApplication.CreateBuilder(args);

// 執行期落點集中解析:dev 維持相對(現狀不動),打包 exe 落 %LOCALAPPDATA%。
// 解析後把絕對路徑寫回既有 config key,讓下方既有 wiring 不動就吃到絕對路徑。
var paths = StoragePaths.Resolve(builder.Environment, builder.Configuration);
Directory.CreateDirectory(paths.BaseDir);   // SQLite 不自建父目錄
Directory.CreateDirectory(paths.LogDir);
builder.Configuration["ConnectionStrings:Pm"] = paths.SqliteDataSource;
builder.Configuration["Thumbnails:Dir"] = paths.ThumbsDir;
builder.Configuration["Inference:Wd14:ModelDir"] = paths.ModelDir;

builder.Services.AddSingleton(new SqliteBusyTimeoutInterceptor(TimeSpan.FromSeconds(5)));
```

> 既有 `BuildSqliteConnectionString(builder.Configuration.GetConnectionString("Pm"))`(行 13)會讀到絕對 `Data Source` 並補 `DefaultTimeout=5`;`GetSection("Thumbnails").Get<ThumbnailOptions>()`(行 17)與 `AddWd14Tagging`(行 32)讀到絕對路徑。皆不需改動。

- [ ] **Step 3: 跑全測試確認綠燈(無回歸)**

Run: `dotnet test`
Expected: 全部 PASS(含 Task 1 的 5 個新測試;既有 API 整合測試因 `Storage__BaseDir` 導到 temp 而不受影響)。

- [ ] **Step 4: 手動驗證 dev 落點不變**

Run: `dotnet run --project src/Pm.Api`(啟動後 Ctrl+C)
Expected:
- `src/Pm.Api/pm.sqlite`、`src/Pm.Api/thumbs/`、`src/Pm.Api/models/`(若存在)**原地不動**,無新副本。
- 出現 `src/Pm.Api/logs/` 目錄(此時可能尚無檔案,Serilog 於 Task 3 接)。
- `/health` 與既有功能正常。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Api/Program.cs tests/Pm.Api.Tests/TestStorageBootstrap.cs
git commit -m "feat(api): 落點收斂走 StoragePaths + 測試 BaseDir 隔離"
```

---

### Task 3: Serilog rolling file logging

**Files:**
- Modify: `src/Pm.Api/Pm.Api.csproj`(加 `Serilog.AspNetCore` PackageReference)
- Modify: `src/Pm.Api/Program.cs`(`UseSerilog` 接線,用 Task 2 的 `paths.LogDir`)

**Interfaces:**
- Consumes: `paths.LogDir`(Task 2)、`builder.Configuration["Logging:LogLevel:Default"]`。

- [ ] **Step 1: 加 Serilog 套件**

Run: `dotnet add src/Pm.Api package Serilog.AspNetCore`
Expected: 還原成功(預期 9.x;內含 Console + File sink,無需另加 sink 套件)。

- [ ] **Step 2: 接線 UseSerilog**

`src/Pm.Api/Program.cs`,在 Task 2 插入的落點解析區塊**之後**、`builder.Services.AddSingleton(new SqliteBusyTimeoutInterceptor(...))` 之前,插入:

```csharp
// Serilog:console(dev 看得到)+ rolling file(落 logs/)。
// 注意:UseSerilog 會繞過 MS Logging:LogLevel 過濾,故 MinimumLevel 必須在此明設;
// 讀 Logging:LogLevel:Default 當 knob(改 appsettings/env + 重啟即可降級,免重編)。
builder.Host.UseSerilog((context, logCfg) => logCfg
    .MinimumLevel.Is(ParseLevel(context.Configuration["Logging:LogLevel:Default"]))
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(paths.LogDir, "pm-.log"),
        rollingInterval: Serilog.RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 50L * 1024 * 1024,
        rollOnFileSizeLimit: true));
```

並把檔案最上方 using 區加入:

```csharp
using Serilog;
```

於檔案底部既有 `static string BuildSqliteConnectionString(...)` 旁(同檔頂層 static helper 區),新增 MS→Serilog 等級對應 helper:

```csharp
// MS Logging level 字串 → Serilog level;解析失敗預設 Information。
static Serilog.Events.LogEventLevel ParseLevel(string? level) => level switch
{
    "Trace" => Serilog.Events.LogEventLevel.Verbose,
    "Debug" => Serilog.Events.LogEventLevel.Debug,
    "Information" => Serilog.Events.LogEventLevel.Information,
    "Warning" => Serilog.Events.LogEventLevel.Warning,
    "Error" => Serilog.Events.LogEventLevel.Error,
    "Critical" => Serilog.Events.LogEventLevel.Fatal,
    _ => Serilog.Events.LogEventLevel.Information,
};
```

- [ ] **Step 3: build + 全測試確認綠燈**

Run: `dotnet build` 然後 `dotnet test`
Expected: build 0 error;測試全 PASS(測試程序的 file sink 寫入隔離 temp;併發次要 logger 拿不到檔鎖時 Serilog 內部吞錯不擲出,不影響測試)。

- [ ] **Step 4: 手動驗證 log 落檔 + 等級 knob**

Run: `dotnet run --project src/Pm.Api`,打開瀏覽器戳 `http://localhost:<port>/health` 幾次,Ctrl+C。
Expected:
- `src/Pm.Api/logs/pm-<yyyymmdd>.log` 產生,內含啟動與 request 訊息;console 同步可見。
- 暫時把 `src/Pm.Api/appsettings.Development.json` 的 `Logging:LogLevel:Default` 改 `"Debug"` 重跑,log 出現更細的 Debug 級訊息;驗畢改回 `"Information"`。

- [ ] **Step 5: Commit**

```bash
git add src/Pm.Api/Pm.Api.csproj src/Pm.Api/Program.cs
git commit -m "feat(api): Serilog rolling file logging(落 logs/,MinimumLevel 讀 config)"
```

---

## Self-Review

**Spec coverage(對照 `2026-06-25-logging-and-app-data-dir-design.md`):**
- §3.1 StoragePaths / BaseDir 三層解析 / 相對 vs 絕對 → Task 1。✓
- §3.2 Program.cs 接線順序 / 建目錄 / 寫回三個 key → Task 2。✓
- §3.3 Serilog 套件 + sinks + rolling/retention + MinimumLevel 讀 config + Microsoft.AspNetCore Override → Task 3。✓
- §3.4 不動 PmDbContextFactory / appsettings 保留 / opt-in gate → 計畫未觸碰(Global Constraints 明列不動)。✓
- §五 測試(5 條 StoragePaths 案例)→ Task 1 Step 1 全覆蓋。✓
- §六 驗收(dev 不搬遷、prod 落 appdata、Debug knob)→ Task 2 Step 4 / Task 3 Step 4。✓
- 補強:整合測試污染風險(spec 未列)→ Task 2 Step 1 TestStorageBootstrap。✓

**Placeholder scan:** 無 TBD/TODO;每個 code step 均含完整程式碼與預期輸出。✓

**Type consistency:** `StoragePaths.Resolve(IHostEnvironment, IConfiguration)`、屬性 `BaseDir/SqliteDataSource/ThumbsDir/ModelDir/LogDir`、helper `ParseLevel(string?)` 在各 Task 間一致。✓
