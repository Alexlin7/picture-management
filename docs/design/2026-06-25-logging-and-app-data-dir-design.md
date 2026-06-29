---
status: active
last-reviewed: 2026-06-29
supersedes: []
superseded-by: []
related: []
---

# 檔案 Logging + App Data Dir 收斂 — 設計

- 日期:2026-06-25
- 狀態:**已實作(2026-06-29 複查確認)** —— `StoragePaths`(app data dir 收斂)+ Serilog rolling file 已落地。
- 範圍:① 引入檔案 logging(Serilog,rolling file);② 把執行期落點(SQLite / 縮圖 / WD14 模型 / log)收斂到單一 **app data dir**,讓單檔 self-contained exe 雙擊執行時資料不散落、log 不消失。
- 來源議題:可觀測性(log 套件 + log 落點)。
- 鐵則對照:不影響 #1(原圖唯讀,衍生資料另放)、#3(SQLite 為 tag 唯一真相)、#8(localhost 單人)。

---

## 一、問題

1. **無檔案 log**:目前只有 ASP.NET Core 預設 **console logging**(`appsettings.json` 僅設 `Logging:LogLevel`,無 file sink)。打包成**單檔 self-contained exe(雙擊、無 console 視窗)後,log 直接消失** —— 掃描錯誤、WD14 失敗、縮圖問題沒有可追的紀錄。
2. **落點全相對工作目錄**:`pm.sqlite`(連線字串 `Data Source=pm.sqlite`)、縮圖 `thumbs`(`ThumbnailOptions.Dir`)、模型 `models/wd14`(`Wd14Options.ModelDir`)都**相對於 process 當前工作目錄**。`dotnet run --project src/Pm.Api` 時工作目錄是專案夾,尚可預期;但雙擊單檔 exe 時工作目錄不確定 → 資料散落、難找。

## 二、決策摘要(brainstorming 已定)

| 決策 | 選定 |
|---|---|
| 範圍 | log **與** app data dir 一起收斂(非只做 log)。 |
| 路徑解析 | **開發維持相對(現狀不動),只打包用 appdata**。 |
| Logging 套件 | **Serilog**,按日 rolling,保留 ~14 天 / 單檔 50MB 上限,console + file 雙 sink。 |
| 落點佈局 | **扁平命名**(沿用今天的 `pm.sqlite` / `thumbs` / `models/wd14`,不改成 `data/` 子夾)→ dev 佈局逐字不變、無資料搬遷。 |

## 三、設計

### 3.1 `StoragePaths` — 單一集中解析點

新增 `src/Pm.Api/StoragePaths.cs`。在 `Program.cs` 建立 service 之前先算好 **BaseDir** 與各子路徑,所有相對落點改吃它算出的**絕對路徑**。

**BaseDir 解析規則(優先序,先命中先用):**

1. config `Storage:BaseDir` 有設(含環境變數 `Storage__BaseDir`)→ 用它(進階覆寫;壓過環境判斷)。
2. 否則 **Development** 環境 → `Directory.GetCurrentDirectory()`(= 現狀;現有 `pm.sqlite` / `thumbs` / `models` 原地不動)。
3. 否則(打包 exe,預設 Production)→ `%LOCALAPPDATA%\sus-picture-management\`
   (= `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` + `"sus-picture-management"`)。

**子路徑(BaseDir 底下,扁平命名):**

| 用途 | 解析結果 | 取用點 |
|---|---|---|
| SQLite | `{base}/pm.sqlite` | 連線字串 `Data Source` |
| 縮圖 | `{base}/thumbs/` | `ThumbnailOptions.Dir` |
| WD14 模型 | `{base}/models/wd14/` | `Wd14Options.ModelDir` |
| **Log(新)** | `{base}/logs/pm-.log` | Serilog file sink path template |

**相對 vs 絕對的處理**:子路徑用「若 config 給的值是相對 → 以 BaseDir 為根組合;若已是絕對 → 原樣保留」的規則(`Path.IsPathRooted` 判斷)。如此 power user 仍可在 appsettings 把單一項指到別處,不被二次前綴。

**建議介面(示意,實作可微調):**

```csharp
public sealed class StoragePaths
{
    public string BaseDir { get; }
    public string SqliteDataSource { get; }   // 絕對 pm.sqlite 路徑
    public string ThumbsDir { get; }          // 絕對
    public string ModelDir { get; }           // 絕對(models/wd14)
    public string LogDir { get; }             // 絕對(logs)

    // env:IHostEnvironment;config:IConfiguration(讀 Storage:BaseDir + 既有 Thumbnails/Inference/ConnectionStrings)
    public static StoragePaths Resolve(IHostEnvironment env, IConfiguration config);
}
```

### 3.2 Program.cs 接線順序

1. `var builder = WebApplication.CreateBuilder(args);`
2. **`var paths = StoragePaths.Resolve(builder.Environment, builder.Configuration);`**
3. `Directory.CreateDirectory(paths.BaseDir);` 與 `Directory.CreateDirectory(paths.LogDir);`
   (SQLite 不自建父目錄;縮圖/模型目錄各自服務已有 `CreateDirectory`,但 BaseDir/LogDir 由此保證存在)。
4. **Serilog 接線**(見 3.3),用 `paths.LogDir`。
5. DbContext 連線字串改用 `paths.SqliteDataSource`(套到既有 `BuildSqliteConnectionString`,仍補 `DefaultTimeout=5`)。
6. `ThumbnailOptions` 註冊時把 `Dir` 覆寫為 `paths.ThumbsDir`。
7. WD14:把 `paths.ModelDir` 餵進 `Wd14Options`(於 `AddWd14Tagging` 解析 options 後覆寫 `ModelDir`,或在註冊前先決定 —— 實作時擇一,不改 opt-in gate 行為)。
8. 啟動 Migrate 前 BaseDir 已存在(步驟 3 保證)。

### 3.3 Serilog 設定

**套件**:`src/Pm.Api/Pm.Api.csproj` 新增 `Serilog.AspNetCore`(內含 Console + File sink,無需另加 sink 套件)。

**接線**:`builder.Host.UseSerilog((context, services, cfg) => { ... })`,**程式化設定**(不引 `Serilog.Settings.Configuration`):

- **Sinks**:
  - Console sink(開發直接看得到)。
  - File sink → `Path.Combine(paths.LogDir, "pm-.log")`,參數:
    - `rollingInterval: RollingInterval.Day`(按日,實際檔名形如 `pm-20260625.log`)。
    - `retainedFileCountLimit: 14`(≈ 保留 14 天)。
    - `fileSizeLimitBytes: 50 * 1024 * 1024`(50MB)+ `rollOnFileSizeLimit: true`(單日超量也滾)。
- **MinimumLevel(關鍵 —— Serilog 坑)**:
  `UseSerilog()` 會把 `ILogger` 呼叫**直接導進 Serilog 管線,繞過** MS `Logging:LogLevel` 過濾;真正生效的是 **Serilog 自己的 `MinimumLevel`**。故**不能**只靠 appsettings 的 `Logging:LogLevel`,必須在 code 明設:
  - 讀 `Logging:LogLevel:Default`(字串)→ 對應 `LogEventLevel`,**找不到或解析失敗則預設 `Information`**。如此使用者仍可「改 appsettings / 環境變數 → 重啟」把全域降到 Debug,**不必重編 exe**(保留 flip-knob,只是真正生效的是 Serilog 端)。
  - `.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)`(鏡射現有意圖,壓掉框架 request 雜訊)。

> **MS ↔ Serilog 等級對應**(自動、固定):Trace↔Verbose、Debug↔Debug、Information↔Information、Warning↔Warning、Error↔Error、Critical↔Fatal。平常照常寫 `_logger.LogInformation(...)`(MS facade),Serilog 自動收。

### 3.4 不動的東西

- **`PmDbContextFactory`**(`src/Pm.Data/PmDbContextFactory.cs`)硬編 `Data Source=pm.sqlite` **維持原樣** —— 只給 `dotnet ef` 設計期工具用,不影響執行期。
- `appsettings.json` 的 `ConnectionStrings:Pm` / `Thumbnails:Dir` / `Inference:Wd14:ModelDir` 保留(作為相對預設值;Production 由 BaseDir 前綴,Development 等同現狀)。
- opt-in gate(`Inference:Wd14:Enabled`)行為不變。

## 四、落點對照(改動前後)

| 環境 | SQLite | 縮圖 | 模型 | Log |
|---|---|---|---|---|
| **Dev(現狀,改動後不變)** | `./pm.sqlite` | `./thumbs/` | `./models/wd14/` | **`./logs/`(新增)** |
| **打包 exe(Production)** | `%LOCALAPPDATA%\sus-picture-management\pm.sqlite` | `…\thumbs\` | `…\models\wd14\` | `…\logs\` |
| **`Storage:BaseDir` 覆寫** | 全部落在指定 base 下 | | | |

## 五、測試(後端 TDD)

新增 `tests/` 內 `StoragePaths` 單元測試(獨立、無 DB):

1. **Development → BaseDir = 當前工作目錄**;各子路徑 = cwd 組合(等同現狀)。
2. **Production(非 Development)→ BaseDir = LocalAppData\sus-picture-management**;子路徑正確組合。
3. **`Storage:BaseDir` 覆寫勝出** —— 設了就用它,壓過環境判斷(Development 與 Production 皆然)。
4. **絕對路徑不被二次前綴** —— config 給絕對 `Thumbnails:Dir` 時,結果原樣保留。
5. **相對路徑以 BaseDir 為根組合** —— config 給相對值時正確接在 BaseDir 後。

> Serilog 接線屬 wiring/infra,不寫脆弱整合測試;改用 `dotnet run` 後肉眼確認 `logs/pm-*.log` 產出且含啟動訊息。

## 六、驗收

- `dotnet build` + `dotnet test` 全綠(含新 `StoragePaths` 測試)。
- `dotnet run --project src/Pm.Api`(Development):`pm.sqlite` / `thumbs` / `models` **原地不動**(無資料搬遷),新增 `logs/pm-<date>.log` 並寫入啟動訊息;掃描/查詢功能不退化。
- 模擬 Production(設 `ASPNETCORE_ENVIRONMENT=Production` 或實際 publish):落點全數移到 `%LOCALAPPDATA%\sus-picture-management\`。
- 調 `Logging:LogLevel:Default=Debug` 重啟後,log 等級確實降到 Debug(驗證 MinimumLevel 有讀 config)。

## 七、不在本次範圍(YAGNI / 後續)

- 把扁平命名改成 appdata 內 `data/ thumbs/ models/ logs/` 整齊子夾(需一次性搬 dev db,刻意不做)。
- 單檔 self-contained publish 的完整驗證(handoff §5;本設計使其落點正確,但 publish 流程驗證另計）。
- 結構化 log 查詢 / 外部 log 匯出 / 遠端 sink。
- log 內容稽核(替既有掃描/WD14/縮圖流程補更細的 log 點)—— 可後續漸進加,不阻塞本地基。
