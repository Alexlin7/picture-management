# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案

單一使用者本機**圖片管理系統**(Windows 11)。圖庫十萬量級、以動漫圖為主、少量個人照片。核心:把「邏輯分類(tag)」跟「檔案系統」徹底脫鉤 —— 就地索引、用 tag 與布林查詢看圖,資料夾只是眾多 tag 軸之一。

**完整設計與所有決策理由在 `docs/superpowers/specs/2026-06-21-picture-management-design.md` —— 動手前先讀它。** 本檔只摘大方向與不可違反的鐵則。UI/UX 見該文件 §6 與可點 mockup `docs/mockups/ui-preview.html`(瀏覽器開,`?view=` / `?only=inspector` 可切換截圖)。

**狀態(2026-06-25):Phase 1 核心可端對端運作;持續 UI/UX 與資料層演進** —— 後端掃描/對帳/查詢/路徑→tag/saved search/facet/軟硬刪/manual tag/標籤庫端點皆完成且有測試;前端各頁已接真實 API(非 mock);單一 .NET 程序 serve API + 前端。**WD14 自動標籤端到端就緒(opt-in)**,已在真實圖庫以 DirectML 實機驗證;CUDA/Windows ML 推論後端僅骨架(本 build 僅 cpu/directml);Phase 2(CLIP 語意搜尋)未開始。近期已落地:**WD14 tag 顯示層 v1**、**UI 樣式系統地基(Spec 1)**、**gallery 頂端操作 UX(Spec 3)**、**async scan + SQLite 硬化**、**作品軸(WD14 copyright 拆分 + facet「作品→角色」DAG 樹)**、**logging + app data dir(log 級別走 appsettings,EF SQL 壓 Warning)**、**孤兒 photo 清理**、**資料夾路徑維度瀏覽 `/browse`(與 by-tag 搜尋並列的第二維度;即時樹 + 麵包屑 + 子夾下鑽 + 夾內疊 tag)**。已知限制:**AVIF 未支援解碼**(ImageSharp 3.x;會被索引但無縮圖/尺寸/自動標)。**現況、啟動方式、逐項功能狀態見根目錄 [`README.md`](README.md);當前 backlog 與接手順序見 `docs/superpowers/specs/2026-06-22-remaining-work-handoff.md`、設計索引見 `docs/superpowers/specs/README.md`。**

## 架構大方向

> **2026-06-21 單程序收斂:** 原「.NET + Python worker + Postgres/Docker」三件套,經紅隊檢視收斂為**單一 .NET 程序 + 嵌入式 SQLite + ONNX 在程序內推論**(理由見 spec §2 修訂註與 §7 決策日誌)。Python/Postgres/Docker 退為日後 NAS/多人的可選路徑。

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
- **`tagging_job` 表保留**作程序內持久佇列(`Channels`+`BackgroundService`),亦是未來 Python compute sidecar 的可重開 seam(無狀態、POST 回 API、不直連 DB)。
- 資料模型九表,身分/位置兩層拆開(`photo` ↔ `photo_location`)。ER 與 DDL 見設計文件 §4。

## 不可違反的鐵則(改動前必讀)

1. **絕不修改、搬動、改名原始圖檔,絕不把 metadata 寫回圖檔(不寫 XMP)。** 原檔一律唯讀 —— PNG 可能藏惡意內容,改檔有風險。衍生資料(縮圖)放 app 自有快取目錄。
2. **`file_hash`(SHA-256)是身分,`file_path` 只是位置。** 搬移/換碟/副本/去重一律靠 `photo_location` 處理,`photo` 身分不動。不要用路徑當主鍵或身分。
3. **SQLite 檔是 tag 的唯一真相(無 XMP)。** 備份 = 複製 `.sqlite`(`VACUUM INTO` 熱備)+ tag manifest 匯出(`hash,tag` 獨立檔),別弱化 —— 避免策展綁死單一 app。
4. **刪除是軟刪**(位置標 `archived`,保留 photo+tags;同 hash 回來自動復原)。只有使用者明示才硬刪 purge。
5. **tag 來源要分**:`photo_tag.source` ∈ path/manual/wd14,WD14 帶 `confidence`。不要把自動標籤跟手動策展混為一談。
6. **ML 推論在 .NET 程序內走 ONNX Runtime**,EP 經 `IInferenceSessionFactory` 抽象,**預設 DirectML**(跨 NVIDIA/AMD,兩台機器不同顯卡),**不要硬綁 CUDA**;無 GPU 退 CPU。要 NV 全速才另加 CUDA publish profile(程式碼不動)。
7. **ML 不另開程序、不引 broker**:`tagging_job` 表當**程序內** DB-backed 佇列。若日後真要 Python,以**無狀態 sidecar**(POST 回 API、不直連 SQLite)接回,別讓兩程序搶寫 SQLite。
8. **單機單人:API 只 bind `localhost`,不做帳號/認證系統。** 一旦改 bind 離 localhost(NAS/多人),認證即從可選變必須。
9. **路徑→tag 是「匯入後確認」**,確認結果存 `path_tag_rule`(每段只確認一次)。不要改成全自動硬塞。
10. **此專案一定要開 SQLite FK cascade。** 全 app 的硬刪(`photo` purge → `photo_location`/`photo_tag`/`tagging_job`;`tag` 刪除/合併 → `photo_tag`/`tag_relation`)都倚賴 DB 層 `ON DELETE CASCADE`,程式碼**不逐表手動刪**。但 SQLite 因歷史相容預設 `foreign_keys=OFF`、且為**連線層 runtime 設定**(不存 DB 檔),故必須在每條連線強制開 —— 唯一真相源是 `Program.cs` 的 `BuildSqliteConnectionString`(`ForeignKeys = true`),測試連線字串亦流經此函式。**絕不關閉**:一旦 FK off,所有硬刪會留下指向不存在父列的懸空子列。新刪除路徑沿用 cascade,不要回頭手刻子表刪除。

## 工具鏈(已確認可用,2026-06-21)

| 工具 | 版本 | 用途 |
|---|---|---|
| .NET SDK | `10.0.301` | ASP.NET Core 後端 + 掃描器 + ONNX 推論(單一程序) |
| Node / npm | `24.15.0` / `11.12.1` | Angular 前端(`ng build` 產靜態檔給 .NET serve) |

關鍵 NuGet:`Microsoft.EntityFrameworkCore.Sqlite`(10.x)、`Microsoft.ML.OnnxRuntime.DirectML`(1.24.x)、`MetadataExtractor`、`SixLabors.ImageSharp`。WD14 模型抓 SmilingWolf 的 `wd-vit/swinv2-tagger-v3`(HF ONNX),以 HTTPS 下載模型 + `selected_tags.csv`。**Phase 1 不需要 Docker / Python**(Docker 僅日後 NAS 包裝;Python 僅未來可選 sidecar)。

## 交付 / 安裝(單一 exe 為主)

- **自包含單檔 exe(主力)**:`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` —— SQLite、DirectML、Angular 靜態檔全包進去,雙擊即開,免裝任何 runtime/DB/Docker/Python。
- **安裝版(可選)**:Velopack / Inno 包裝同一顆 exe(捷徑、自動更新)。
- **Docker image(可選,僅日後 NAS/Linux headless)**:Linux 內推論退 CPU(DirectML 為 Windows-only)。

## 指令(已可用;啟動細節與整合流程見 README.md)

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
- **計畫先行(review-first):動任何 code 前,先提出計畫或設計文件,等使用者確認再實作** —— 尤其重構、新功能、相依/設定變更。未經同意不要開始 implementation。
- **小切片、逐步 commit**:重構/抽取時切成連續小切片,每片 build + 測試綠後再 commit,不要一大刀塞完。
- **commit / merge 前先驗證**:多檔變更後跑 `dotnet build` + `dotnet test`(全測試),確認綠燈才 commit/merge;前端改完跑 `ng build` + 起 app 手測(前端自動測試覆蓋有限)。

**慣例:**
- 全程以**繁體中文(台灣用語)** 溝通;程式碼識別子與技術名詞保留原文。
- **後端測試 DB 隔離**:每測試用獨立 SQLite 檔(`Data Source={tmp}`)或 `:memory:`(見 `tests/` 既有測試),避免互相污染 —— 本專案是 EF Core + SQLite,**無** Java `@Transactional` 那套。
- **設定分層**:能力層開關(載不載模型等)走 `appsettings` / Rider launchSettings 啟動參數(改了要重啟);行為層執行期開關走 `app_setting`(見 `docs/superpowers/specs/2026-06-23-scanner-tagging-refactor-design.md`)。
- schema/設計可演進;改動牽涉設計決策時,同步更新 `docs/superpowers/specs/` 設計文件。

**前端樣式慣例(Tailwind v4 + Angular 隔離編譯;完整理由見 `docs/superpowers/specs/2026-06-24-ui-style-system-design.md`):**
- **新樣式落點決策樹**:
  1. 能用 Tailwind utility 表達(間距/排版/顏色/簡單狀態)→ 寫在 **template 的 `class`**。
  2. 會**跨元件重複**的元件樣式 → 進 `src/Pm.Web/src/styles.css` 的 `@layer components` 共用 primitive(`.btn`/`.input`/`.skeleton`… 可 `@apply`)。
  3. 元件**專屬且複雜**(動畫、複雜 selector、RWD 條件)→ 元件 `.css`,**一律用 `var(--token)`**,不放能用 utility 表達的瑣碎樣式。
  4. **顏色/字體/圓角/陰影/elevation 一律走 token**(`styles.css` `@theme`,同時產 utility 與 `var`),不寫裸 hex。
- **鐵則**:元件 `.css`(component-scoped)**不得** `@apply`/`@tailwind`/`@reference` —— Angular 隔離編譯選不到全域 token,故元件 .css 只能手寫 + `var(--token)`。共用 `@apply` 只能寫在全域 `styles.css`。
- **a11y 基礎已就位**:全域 `:focus-visible` cyan ring + `prefers-reduced-motion` 降載;新互動元件勿用 `outline:none` 蓋掉 focus ring(除非容器自理 focus)。
