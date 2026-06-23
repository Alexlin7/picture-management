# Picture Management（圖片管理系統）

單一使用者本機**圖片管理系統**（Windows 11）。圖庫十萬量級、以動漫圖為主、少量個人照片。
核心：把「邏輯分類（tag）」跟「檔案系統」徹底脫鉤 —— 就地索引、用 tag 與布林查詢看圖，
資料夾只是眾多 tag 軸之一。

> 完整設計與決策理由在 [`docs/superpowers/specs/2026-06-21-picture-management-design.md`](docs/superpowers/specs/2026-06-21-picture-management-design.md)。
> 不可違反的鐵則與工具鏈在 [`CLAUDE.md`](CLAUDE.md)。本檔講「現在長怎樣、怎麼跑、做到哪」。

---

## 目前狀態（2026-06-22）

**Phase 1 核心可端對端運作**：掃描就地索引 → SHA-256 身分 → 縮圖 → 布林查詢 → Angular 相簿，
前端各畫面**已接真實 API**（非 mock），單一 .NET 程序同時 serve API 與前端。

- ✅ 後端 scan / 對帳 / 查詢 / 路徑→tag / saved search / facet 樹 / 軟硬刪 / manual tag 端點（皆有測試）
- ✅ 標籤庫端點：列表（含使用數）/ 改名 / 合併 / 刪除，正規化 + **全 Unicode 不分大小寫去重**（`name_ci` 鍵 + 唯一索引，非 ASCII 角色/作品名也去重）（`TagService`）
- ✅ 前端 gallery / import / reconcile / saved / roots + inspector + **標籤庫管理頁 `/tags`** 接真實 API（實機驗證）；檢視器以 combobox 加/刪標籤（查既有、防近似重複）
- ✅ **WD14 自動標籤端到端就緒（opt-in）**：ML pipeline（前處理 448² BGR / WD14 ONNX in-proc / 後處理門檻）＋ `TaggingWorker` 已透過 `AddWd14Tagging` wire 進 host —— `Inference:Enabled=true` 時註冊推論工廠＋tagger＋背景服務消化 `tagging_job`，**預設關閉**（免下載模型）；worker 寫 tag 走 `TagService`（全 Unicode CI 去重、kind 語意升級不降級、與 manual 共用 `AttachTag` 路徑），不再與手動標籤撞大小寫或畫錯 lane；崩潰留下的 `running` job 啟動時自動回收重排
- ✅ **WD14 已在真實圖庫實機驗證（2026-06-22，AMD RX 9060 XT / DirectML）**：200 張動漫圖一輪 0 失敗；HF 模型 + `selected_tags.csv` 自動下載成功；DirectML 在 AMD GPU 推論成功；標籤品質佳（`1girl`/`long_hair`/`halo`/角色名…），`source=wd14`＋`confidence` 正確寫入。**category↔kind 對應確認無誤**：WD14 v3 的 csv 只有 category `0`(general)/`4`(character)/`9`(rating，被過濾)，無 `3`(copyright)，故 `KindOf` 的 `3→copyright` 為不觸發死碼（無害；作品名內嵌於角色標如 `aris_(blue_archive)`）。門檻 general 0.35 / character 0.85 表現合理（character 偏高精度，想多召回可調低）
- 🚧 推論後端：CPU / DirectML 可用；CUDA、Windows ML 僅骨架（見 `src/Pm.Ml`）
- 🔲 Phase 2（CLIP 語意搜尋）未開始

詳細逐項見下方 [功能狀態](#功能狀態)。

---

## 架構

```
Angular SPA
    ↓ REST（同源 localhost；ng serve 開發時走 proxy）
Pm.Api（啟動、DI、API endpoints、serve 前端靜態檔）
    ├─ Pm.Scanner（掃描、縮圖、EXIF、查詢、tag closure/facet、路徑→tag —— 實為 application/service 層）
    ├─ Pm.Data（EF Core、SQLite、九張 Entities、migrations）
    └─ Pm.Ml（ONNX 推論後端抽象 IInferenceSessionFactory；目前 CPU/DirectML 可用，CUDA/WinML 骨架）
```

- **單一 .NET 程序**：API + 內建掃描器 +（未來）ONNX 推論。無第二程序、無 broker、無 server DB。
- **嵌入式 SQLite** 是 tag 的唯一真相（無 XMP）。
- **`Pm.Scanner` 命名說明**：它現在不只「掃描」，還含查詢 / tag / 縮圖 / metadata，比較像 `Pm.Core`/service 層；
  名稱待日後重新界定（見 CLAUDE.md）。

---

## 如何啟動

需求：.NET SDK `10.0.301`、Node `24.x` / npm（見 CLAUDE.md 工具鏈表）。

### A. 一鍵整合（後端 serve 已 build 的前端）
```powershell
# 1) 先 build 前端，輸出進 Pm.Api/wwwroot
cd src/Pm.Web ; npm ci ; npm run build
# 2) 起後端（同時 serve API 與前端於 http://localhost:5180）
cd ../.. ; dotnet run --project src/Pm.Api
```
瀏覽器開 `http://localhost:5180`。首次啟動自動建 `pm.sqlite`、套 migration。

### B. 前端熱重載開發（前後端分開跑）
```powershell
dotnet run --project src/Pm.Api                 # 後端 :5180
cd src/Pm.Web ; npm start                        # ng serve :4200，/api 經 proxy.conf.json 轉 :5180
```
開發看 UI 走 `http://localhost:4200`。

### 匯入圖庫（目前透過 API）
```powershell
# 註冊一個來源資料夾（Windows 路徑用正斜線免跳脫）
curl -X POST http://localhost:5180/api/roots -H "Content-Type: application/json" `
     -d '{"name":"my-lib","absPath":"D:/pics"}'
# 觸發掃描（就地索引，絕不搬動原檔）
curl -X POST http://localhost:5180/api/roots/1/scan -H "Content-Type: application/json" -d '{}'
```
之後在 UI 的「匯入確認」頁確認路徑→tag 規則。

### 啟用 WD14 自動標籤（opt-in，預設關）
在 `src/Pm.Api/appsettings.json`（或環境變數）把 `Inference:Wd14:Enabled` 設為 `true`，並視硬體設 `Inference:Wd14:Backend`（`directml` 預設 / `cpu` / 日後 `cuda`）。開啟後啟動程序即註冊 `TaggingWorker` 背景服務，會消化掃描排入的 `tagging_job`；**首次標註會自動 HF 下載 WD14 模型（~300MB）＋ `selected_tags.csv` 到 `Inference:Wd14:ModelDir`（預設 `models/wd14`）**。關閉時零開銷、不下載。能力開關按模型獨立（`Inference:Wd14:*`，未來 CLIP 走 `Inference:Clip:*`）。

```powershell
# 例:以環境變數臨時開啟(走 CPU)
$env:Inference__Enabled = "true"; $env:Inference__Backend = "cpu"
dotnet run --project src/Pm.Api
```

### 測試
```powershell
dotnet test                       # 後端全測試
cd src/Pm.Web ; npm test          # 前端
```

---

## 資料放哪

| 東西 | 位置 | 備註 |
|---|---|---|
| 資料庫（唯一真相） | `pm.sqlite`（程序工作目錄） | 備份＝複製此檔；勿弱化 |
| 縮圖快取 | `thumbs/`（依 `Thumbnails:Dir` 設定，依 hash 分桶） | 衍生資料，可重建；512px webp |
| 原始圖檔 | 你的來源資料夾 | **唯讀，絕不修改/搬移/改名，不寫 XMP** |

---

## 功能狀態

### ✅ 已完成且驗證
- SQLite 九表 schema + migration；身分（`photo`）/位置（`photo_location`）兩層分離
- 掃描器：走訪 + SHA-256 + upsert 身分/位置 + 同內容去重 + size/mtime 快路徑 + EXIF/尺寸/MIME + 512px webp 縮圖 + 走訪後對帳（missing/搬移/失蹤/復原）
- 路徑→tag：待確認段收集 + 確認規則 + 重掃自動套用
- 查詢：`TagClosureService`（DAG 後代閉包 recursive CTE）+ `PhotoQueryService`（布林 AND/排除 + keyset，只回 present）
- API：roots(GET/POST)、scan、search、photos/{id}(+thumb)、reconcile/missing(+archive 軟刪/purge 硬刪)、path-rules、saved-searches(CRUD)、tags/tree(facet)、photos/{id}/tags(manual 新增/刪除)
- 標籤庫：`TagService`（`Normalize` 收合空白、不強制小寫；`UpsertByNameAsync` 走 `name_ci` 全 Unicode CI 去重 + kind 語意升級不降級 + 拒空白名；`AttachTagAsync` 共用加 photo_tag 路徑；list/rename/merge/delete；改名/列表 limit 推 SQL）+ `GET /api/tags`、`PUT/DELETE /api/tags/{id}`、`POST /api/tags/{id}/merge/{targetId}`（空白名回 400）+ SQLite 開 WAL（背景 worker 與 API 雙寫入）
- 前端各頁接真實 API + 真實縮圖 + 點圖→檢視器真實 detail；標籤庫管理頁 `/tags`（列表/過濾/改名→撞名自動合併/刪除）；檢視器 combobox 加/刪標籤（查既有、↑↓/Enter/Esc、僅無 CI 完全相符才顯示「建立新標籤」）；單程序 serve SPA
- 推論後端抽象 `IInferenceSessionFactory`（CPU / DirectML 可用）
- WD14 ML pipeline（`Pm.Ml`）：`Wd14Preprocess`（方形白底 padding→448²→BGR NHWC）、`Wd14Tagger`（lazy load + session 重用 ONNX in-proc）、`Wd14Postprocess`（general/character 門檻、category→kind）、`Wd14ModelProvider`（HF 下載模型 + selected_tags.csv）
- `TaggingWorker`（抽 `tagging_job`→推論→經 `TagService` CI 去重 + `AttachTag` 寫 `photo_tag(source=wd14)`→done/error；預載既有 tagId 消 N+1；啟動回收孤兒 `running`）＋ `AddWd14Tagging` host wiring（`Inference:Wd14:Enabled` opt-in gate，預設關；backend→factory 自 `.Backend` 查找）—— 皆有測試
- 模型供應 `Wd14ModelProvider`：HF 下載走 `.part` 暫存檔 + atomic rename，中斷不留壞檔

### ⚠️ 已接 API 但功能簡化（API 無來源，刻意 deferred，非 bug）
- 相簿：總命中數暫顯「已載入數」、WD14 佇列暫顯 0、per-tile 的 tag chips/系列/去重/個人照片標記已移除、搜尋框送出加 token 互動未接、無初始查詢
- 檢視器：WD14 建議（✓✕ 採用/拒絕）移除、系列/個人/GPS 移除；manual tag 新增刪除 store 有方法但尚無 UI
- 管理：來源檔數/掃描時間/狀態點、reconcile 已續接數、saved 命中數/特殊卡隱藏；新增來源用 prompt 收路徑（無資料夾挑選器）；saved 點卡套用查詢未接

### 🔲 尚未實作 / 待驗
- **WD14 失敗 job 無自動重試**：`TaggingWorker` 失敗只標 `error` ＋ `Attempts++`，不自動重排（之後可加退避重試）。註：崩潰卡在 `running` 的 job 啟動時**會**自動回收重排（與此不同）
- CUDA / Windows ML 推論後端（`Pm.Ml` 僅骨架；本 build 僅 cpu/directml，選到 cuda/winml 會明確報錯；Windows ML 為 Phase 2 待啟用）
- 單檔自包含 exe 交付（`dotnet publish PublishSingleFile`）設定與驗證

### 🚧 Phase 2（規劃）
- CLIP image embedding 語意搜尋（sqlite-vec 或遷 Postgres+pgvector）

---

## 專案結構

```
src/
  Pm.Api/       ASP.NET Core 宿主：DI、migration、API endpoints、serve wwwroot；Program.cs 為端點樞紐；TaggingWorker（背景服務）+ Wd14Setup（opt-in host wiring）
  Pm.Scanner/   掃描 + 縮圖 + EXIF + 查詢 + tag closure/facet + 路徑→tag + TagService（標籤庫）（service 層）
  Pm.Data/      EF Core：Entities/（九實體）、PmDbContext、Migrations/
  Pm.Ml/        推論後端抽象：IInferenceSessionFactory + Cpu/DirectMl 實作 + Cuda/WindowsML 骨架；WD14 pipeline（Wd14Preprocess/Tagger/Postprocess/ModelProvider）
  Pm.Web/       Angular（core / features / shell / testing 分區；ng build 輸出至 ../Pm.Api/wwwroot）
tests/          與 src 一一對應的測試專案（Pm.Data/Api/Ml/Scanner.Tests）
docs/           設計 spec、實作計畫、UI mockup
```
