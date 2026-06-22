# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案

單一使用者本機**圖片管理系統**(Windows 11)。圖庫十萬量級、以動漫圖為主、少量個人照片。核心:把「邏輯分類(tag)」跟「檔案系統」徹底脫鉤 —— 就地索引、用 tag 與布林查詢看圖,資料夾只是眾多 tag 軸之一。

**完整設計與所有決策理由在 `docs/superpowers/specs/2026-06-21-picture-management-design.md` —— 動手前先讀它。** 本檔只摘大方向與不可違反的鐵則。UI/UX 見該文件 §6 與可點 mockup `docs/mockups/ui-preview.html`(瀏覽器開,`?view=` / `?only=inspector` 可切換截圖)。

**狀態(2026-06-22):Phase 1 核心可端對端運作** —— 後端掃描/對帳/查詢/路徑→tag/saved search/facet/軟硬刪/manual tag/標籤庫(列表/改名/合併/刪除,全 Unicode CI 去重 via `name_ci`)端點皆完成且有測試;前端各頁(含標籤庫管理頁 `/tags`、檢視器 combobox 加/刪標籤)已接真實 API(非 mock);單一 .NET 程序 serve API + 前端。**WD14 自動標籤端到端就緒(opt-in)**:ML pipeline(前處理/ONNX in-proc 推論/後處理)＋ `TaggingWorker` 經 `AddWd14Tagging` wire 進 host,`Inference:Enabled=true` 才註冊推論工廠＋tagger＋背景服務消化 `tagging_job`(**預設關,免下載模型**);worker 寫 tag 走 `TagService`(CI 去重、kind 語意升級不降級、啟動回收孤兒 `running`)。**尚未在真實圖庫實機驗證**(首次標註會 HF 下載模型;category↔csv 對應與門檻待校正)。CUDA/Windows ML 推論後端僅骨架(本 build 僅 cpu/directml);Phase 2(CLIP 語意搜尋)未開始。**現況、啟動方式、逐項功能狀態見根目錄 [`README.md`](README.md)。**

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

- 全程以**繁體中文(台灣用語)** 溝通;程式碼識別子與技術名詞保留原文。
- schema/設計可演進;改動牽涉設計決策時,同步更新 `docs/superpowers/specs/` 設計文件。
