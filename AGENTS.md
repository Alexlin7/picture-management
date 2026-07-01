# AGENTS.md

本檔給 coding agent(Claude Code / Codex / 其他)使用,是專案的大方向、不可違反的鐵則與開發約定的單一真相源。

## 專案

單一使用者本機**圖片管理系統**(Windows 11)。圖庫十萬量級、以動漫圖為主、少量個人照片。核心:把「邏輯分類(tag)」跟「檔案系統」徹底脫鉤 —— 就地索引、用 tag 與布林查詢看圖,資料夾只是眾多 tag 軸之一。

**完整設計與所有決策理由在 [`docs/design/2026-06-21-picture-management-design.md`](docs/design/2026-06-21-picture-management-design.md) —— 動手前先讀它。** 本檔只摘大方向與鐵則。現況、啟動方式、功能狀態見根目錄 [`README.md`](README.md);設計索引見 [`docs/design/README.md`](docs/design/README.md);UI/UX 見主設計 §6 與可點 mockup `docs/mockups/ui-preview.html`。

## 架構大方向

```
Angular SPA(ng build 靜態檔)──REST(localhost)──> 單一 .NET 程序
   └─ ASP.NET Core API + Scanner(背景服務:hash/EXIF/路徑→tag/搬移偵測/縮圖)
      + 標籤背景服務(抽 tagging_job → WD14 ONNX in-proc)
      + IInferenceSessionFactory(DirectML / 日後 CUDA / CPU)
   ↓                ↓
   SQLite 檔(單一真相)   縮圖快取(依 hash,絕不碰原圖)
```

- **單一 .NET 程序**:Angular 前端(由 .NET serve 靜態檔)、C#/.NET 後端(API + 內建掃描器 + ONNX 推論)。無第二程序、無 broker、無 server DB。
- **嵌入式 SQLite** 扛布林多軸查詢 + JSON(EXIF)+ FTS5 + recursive CTE;單程序天然序列化寫入。Phase 2 語意搜尋走 sqlite-vec 或遷 Postgres+pgvector。
- **`tagging_job` 表** 作程序內持久佇列(`BackgroundService` 輪詢 + DB 佇列),亦是未來 Python compute sidecar 的可重開 seam(無狀態、POST 回 API、不直連 DB)。
- 資料模型九表,身分/位置兩層拆開(`photo` ↔ `photo_location`)。ER 與 DDL 見主設計 §4。

## 不可違反的鐵則(改動前必讀)

1. **絕不修改、搬動、改名原始圖檔,絕不把 metadata 寫回圖檔(不寫 XMP)。** 原檔一律唯讀 —— PNG 可能藏惡意內容,改檔有風險。衍生資料(縮圖)放 app 自有快取目錄。
2. **`file_hash`(SHA-256)是身分,`file_path` 只是位置。** 搬移/換碟/副本/去重一律靠 `photo_location` 處理,`photo` 身分不動。不要用路徑當主鍵或身分。
3. **SQLite 檔是 tag 的唯一真相(無 XMP)。** 備份 = 複製 `.sqlite`(`VACUUM INTO` 熱備)+ tag manifest 匯出(`hash,tag` 獨立檔),別弱化 —— 避免策展綁死單一 app。
4. **刪除是軟刪**(位置標 `archived`,保留 photo+tags;同 hash 回來自動復原)。只有使用者明示才硬刪 purge。
5. **tag 來源要分**:`photo_tag.source` ∈ path/manual/wd14,WD14 帶 `confidence`。不要把自動標籤跟手動策展混為一談。
6. **ML 推論在 .NET 程序內走 ONNX Runtime**,EP 經 `IInferenceSessionFactory` 抽象,**預設 DirectML**(跨 NVIDIA/AMD),**不要硬綁 CUDA**;無 GPU 退 CPU。三個推論 flavor(directml / cuda / windowsml)由**編譯期** `InferenceFlavor` 屬性切套件 + `INFER_*` 常數選 factory(見 `InferenceFactories` / `Pm.Ml.csproj`),各有 publish profile + CI matrix —— 切點是 **OS 版本涵蓋**(windowsml=Win11 24H2+、directml/cuda 補舊系統),**換 flavor 只換 publish profile、呼叫端程式碼不動**。三套 native ORT 互斥,絕不塞同一包。
7. **ML 不另開程序、不引 broker**:`tagging_job` 表當**程序內** DB-backed 佇列。若日後真要 Python,以**無狀態 sidecar**(POST 回 API、不直連 SQLite)接回,別讓兩程序搶寫 SQLite。
8. **單機單人:API 只 bind `localhost`,不做帳號/認證系統。** 一旦改 bind 離 localhost(NAS/多人),認證即從可選變必須。
9. **路徑→tag 是「匯入後確認」**,確認結果存 `path_tag_rule`(每段只確認一次)。不要改成全自動硬塞。
10. **此專案一定要開 SQLite FK cascade。** 全 app 的硬刪都倚賴 DB 層 `ON DELETE CASCADE`,程式碼**不逐表手動刪**。SQLite 預設 `foreign_keys=OFF` 且為連線層 runtime 設定,故必須在每條連線強制開 —— 唯一真相源是 `Configuration/SqliteSetup.cs` 的 `SqliteSetup.BuildConnectionString`(`ForeignKeys = true`);`Pm.Api.Tests` 流經此函式,`Pm.Data.Tests` / `Pm.Scanner.Tests` 因不參照 `Pm.Api`(避免循環相依)另行手動設 `Foreign Keys=True`(效果等價)。**絕不關閉**。

## 工具鏈(已確認可用)

| 工具 | 版本 | 用途 |
|---|---|---|
| .NET SDK | `10.0.301` | ASP.NET Core 後端 + 掃描器 + ONNX 推論(單一程序) |
| Node / npm | `24.15.0` / `11.12.1` | Angular 前端(`ng build` 產靜態檔給 .NET serve) |

關鍵 NuGet:`Microsoft.EntityFrameworkCore.Sqlite`(10.x)、`Microsoft.ML.OnnxRuntime.DirectML`(1.24.x)、`MetadataExtractor`、`SixLabors.ImageSharp`。WD14 模型抓 SmilingWolf 的 `wd-vit/swinv2-tagger-v3`(HF ONNX),以 HTTPS 下載模型 + `selected_tags.csv`。

## 交付 / 安裝(單一 exe 為主)

- **自包含單檔 exe(主力)**:`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` —— SQLite、DirectML、Angular 靜態檔全包進去,雙擊即開。
- **安裝版(可選)**:Velopack / Inno 包裝同一顆 exe。
- **Docker image(可選,僅日後 NAS/Linux headless)**:Linux 內推論退 CPU(DirectML 為 Windows-only)。

完整部署/設定/散布見 [`docs/deployment.md`](docs/deployment.md)。

## 指令(啟動細節見 README.md)

```powershell
# 後端(.NET 單程序,內含 SQLite + ONNX,免外部 DB)
dotnet build ; dotnet test ; dotnet run --project src/Pm.Api

# 前端(Angular)
npm ci ; ng serve              # 開發;ng build 產靜態檔給 .NET serve

# 交付
dotnet publish src/Pm.Api -r win-x64 --self-contained -p:PublishSingleFile=true
```

## 分階段

- **Phase 1(核心)**:schema(SQLite)+ 掃描/對帳 + 路徑→tag 確認 + 布林查詢 + Angular 相簿 + 縮圖 + WD14 in-proc 標籤。**不含** embedding/向量。
- **Phase 2(語意搜尋)**:CLIP image embedding → 向量查詢;儲存走 sqlite-vec 或遷 Postgres+pgvector。

## 開發約定

**工作流程(動手前必讀):**
- **計畫先行(review-first):動任何 code 前,先提出計畫或設計文件,等使用者確認再實作** —— 尤其重構、新功能、相依/設定變更。
- **小切片、逐步 commit**:重構/抽取時切成連續小切片,每片 build + 測試綠後再 commit。
- **commit / merge 前先驗證**:多檔變更後跑 `dotnet build` + `dotnet test`(全測試),確認綠燈才 commit/merge;前端改完跑 `ng build` + 起 app 手測。

**慣例:**
- 全程以**繁體中文(台灣用語)** 溝通;程式碼識別子與技術名詞保留原文。
- **後端測試 DB 隔離**:每測試用獨立 SQLite 檔(`Data Source={tmp}`)或 `:memory:`,避免互相污染 —— 本專案是 EF Core + SQLite,**無** Java `@Transactional` 那套。
- **設定分層**:能力層開關(載不載模型等)走 `appsettings` / launchSettings 啟動參數(改了要重啟);行為層執行期開關規劃走 `app_setting`(**尚未實作**,待前端設定頁需求出現;見 [`docs/design/2026-06-23-scanner-tagging-refactor-design.md`](docs/design/2026-06-23-scanner-tagging-refactor-design.md) §D)。
- schema/設計可演進;改動牽涉設計決策時,同步更新 `docs/design/` 設計文件與必要的 `README.md` / `AGENTS.md`。

**前端樣式慣例(Tailwind v4 + Angular 隔離編譯;完整理由見 [`docs/design/2026-06-24-ui-style-system-design.md`](docs/design/2026-06-24-ui-style-system-design.md) 與 [`docs/design/2026-06-24-frontend-design-guidelines.md`](docs/design/2026-06-24-frontend-design-guidelines.md)):**
- **新樣式落點決策樹**:
  1. 能用 Tailwind utility 表達(間距/排版/顏色/簡單狀態)→ 寫在 **template 的 `class`**。
  2. 會**跨元件重複**的元件樣式 → 進 `src/Pm.Web/src/styles.css` 的 `@layer components` 共用 primitive(`.btn`/`.input`/`.skeleton`… 可 `@apply`)。
  3. 元件**專屬且複雜**(動畫、複雜 selector、RWD 條件)→ 元件 `.css`,**一律用 `var(--token)`**。
  4. **顏色/字體/圓角/陰影/elevation 一律走 token**(`styles.css` `@theme`),不寫裸 hex。
- **鐵則**:元件 `.css`(component-scoped)**不得** `@apply`/`@tailwind`/`@reference` —— Angular 隔離編譯選不到全域 token,故元件 .css 只能手寫 + `var(--token)`。共用 `@apply` 只能寫在全域 `styles.css`。
- **a11y 基礎已就位**:全域 `:focus-visible` cyan ring + `prefers-reduced-motion` 降載;新互動元件勿用 `outline:none` 蓋掉 focus ring(除非容器自理 focus)。

**e2e 測試慣例(完整審查 + 遷移計畫見 [`docs/design/2026-06-29-e2e-test-hardening.md`](docs/design/2026-06-29-e2e-test-hardening.md)):**
- **鐵則(新寫 / 改寫 e2e 一律遵守,既有違反處逐步收斂、不得新增):**
  1. **一律 web-first 斷言** `await expect(locator).toXxx()`;**禁** `expect(await …)` 與「讀一次再手動比較」(`const x = await …; if (x !== …) fail()`)。
  2. **禁硬等**(`waitForTimeout` / `sleep`);一律等具體條件(`expect` 輪詢 / `waitForFunction` / `locator.waitFor({state})`)。視覺截圖前的短等需註明理由。
  3. **Locator 優先序** `getByRole` > `getByLabel`/`getByText` > `getByTestId` > CSS;有 role/aria 的元件一律 `getByRole`。**禁** ElementHandle(`page.$`/`$$`/`$eval`/`$$eval`,不 auto-wait)。
  4. **禁 `force:true`**(除非註明非掩蓋真問題);URL 走 config `baseURL`,測試內不散落 `localhost`。
  5. **測試隔離**:每個 `test()` 可單獨重跑、`page.route` mock 在 `beforeEach` 重建;單一 test 聚焦單一行為,不塞過載斷言。
  6. **全程 `page.route` mock `/api`**,守鐵則 #1(不依賴真實圖庫 / 不碰原圖);物件型端點回正確形狀空物件而非 `[]`。
- **目標形態**:`@playwright/test` runner + `playwright.config.ts`(`baseURL` / `webServer` 自動起 app / `testIdAttribute='data-testid'`);既有 5 支 `.mjs` 煙霧腳本分階段遷移,**新測試直接照鐵則寫、不再新增手寫腳本**。

## 文件整理原則

- `README.md`:現況、啟動方式、功能清單(公開門面)。
- `AGENTS.md`:大方向、鐵則、開發約定(本檔)。
- `docs/design/`:需保留的設計決策與脈絡;已完成的瑣碎實作細節不要堆在這裡造成噪音。
- **文件治理(權威 / 時序 / supersede / frontmatter)**:規則統一在 [`docs/design/README.md`](docs/design/README.md#文件治理權威--時序--metadata)。重點:① 內容衝突時 `AGENTS 鐵則 > 主設計 §7 > 分項設計 > README`;② 改設計決策要回寫 canonical(主設計 §7 或本檔),舊文件加 `superseded-by`、留理由不刪;③ 新設計文件頂端要加 YAML frontmatter(`status`/`last-reviewed`/`supersedes`/`superseded-by`/`related`)。
