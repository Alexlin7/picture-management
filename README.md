# Picture Management(圖片管理系統)

單一使用者的本機**圖片管理系統**(Windows 11),為十萬量級、以動漫圖為主的圖庫而設計。

核心理念:把「邏輯分類(tag)」跟「檔案系統」徹底脫鉤 —— **原圖就地索引、絕不搬動**,以 tag 與布林查詢看圖,資料夾只是眾多 tag 軸之一。

> 完整設計與決策理由見 [`docs/design/2026-06-21-picture-management-design.md`](docs/design/2026-06-21-picture-management-design.md);
> 不可違反的鐵則與開發約定見 [`AGENTS.md`](AGENTS.md);部署/散布見 [`docs/deployment.md`](docs/deployment.md)。

---

## 特色

- **身分與位置分離**:`file_hash`(SHA-256)是圖片身分,`file_path` 只是位置。搬移、換碟、副本、去重都不影響 tag。
- **原檔唯讀**:絕不修改、搬動、改名原始圖檔,也不把 metadata 寫回圖檔(不寫 XMP)。衍生資料(縮圖)放 app 自有快取。
- **SQLite 是 tag 的唯一真相**:布林多軸查詢(AND / 排除)+ DAG 標籤閉包 + facet 樹,全在嵌入式 SQLite。
- **多維檢視**:by-tag 搜尋與「資料夾路徑維度」瀏覽 `/browse` 並列,互不干擾。
- **WD14 自動標籤(opt-in)**:ONNX Runtime 在 .NET 程序內推論,預設走 **DirectML**(跨 NVIDIA / AMD),無 GPU 退 CPU。tag 來源分 `path` / `manual` / `wd14`,自動標帶 confidence,不與手動策展混淆。
- **軟刪優先**:刪除預設為軟刪(標 archived,保留 photo + tags),同 hash 回來自動復原;只有明示才硬刪 purge。
- **單一程序、單檔交付**:Angular 前端由 .NET 程序 serve,連同 SQLite、DirectML、ONNX 全包進一顆 win-x64 自包含 exe,雙擊即跑,免裝 runtime / DB。

---

## 架構

```
Angular SPA
    ↓ REST(同源 localhost;ng serve 開發時走 proxy)
Pm.Api(啟動、DI、API endpoints、serve 前端靜態檔)
    ├─ Pm.Scanner   掃描、縮圖、EXIF、查詢、tag closure/facet、路徑→tag(service 層)
    ├─ Pm.Data      EF Core、SQLite、九張 Entities、migrations
    ├─ Pm.Ml        ONNX 推論後端抽象 IInferenceSessionFactory + WD14 pipeline
    └─ Pm.Imaging   AVIF/HEIC/HEIF 解碼橋接(Magick.NET/libheif → ImageSharp)
```

- **單一 .NET 程序**:API + 內建掃描器 + ONNX 推論(WD14 in-proc,opt-in)。無第二程序、無 broker、無 server DB。
- **嵌入式 SQLite** 是 tag 的唯一真相(無 XMP)。單程序天然序列化寫入,並開 WAL 供背景 worker 與 API 雙寫。

---

## 如何啟動

需求:.NET SDK `10.0.301`、Node `24.x` / npm。

### A. 一鍵整合(後端 serve 已 build 的前端)
```powershell
# 1) 先 build 前端,輸出進 Pm.Api/wwwroot
cd src/Pm.Web ; npm ci ; npm run build
# 2) 起後端(同時 serve API 與前端於 http://localhost:5180)
cd ../.. ; dotnet run --project src/Pm.Api
```
瀏覽器開 `http://localhost:5180`。首次啟動自動建 `pm.sqlite`、套 migration。

### B. 前端熱重載開發(前後端分開跑)
```powershell
dotnet run --project src/Pm.Api          # 後端 :5180
cd src/Pm.Web ; npm start                 # ng serve :4200,/api 經 proxy 轉 :5180
```

### 匯入圖庫(目前透過 API)
```powershell
# 註冊一個來源資料夾(Windows 路徑用正斜線免跳脫)
curl -X POST http://localhost:5180/api/roots -H "Content-Type: application/json" `
     -d '{"name":"my-lib","absPath":"D:/pics"}'
# 觸發掃描(就地索引,絕不搬動原檔);POST 回 202,結果由 scan-status 輪詢
curl -X POST http://localhost:5180/api/roots/1/scan -H "Content-Type: application/json" -d '{}'
curl http://localhost:5180/api/roots/1/scan-status
```
之後在 UI 的「匯入確認」頁確認路徑→tag 規則。

### 啟用 WD14 自動標籤(opt-in,預設關)
把 `Inference:Wd14:Enabled` 設為 `true`(`src/Pm.Api/appsettings.json` 或環境變數),並視硬體設 `Inference:Wd14:Backend`(`directml` 預設 / `cpu`)。**首次標註會自動下載 WD14 模型(~300MB)+ `selected_tags.csv`**;關閉時零開銷、不下載。

```powershell
# 例:以環境變數臨時開啟(走 CPU)
$env:Inference__Wd14__Enabled = "true"; $env:Inference__Wd14__Backend = "cpu"
dotnet run --project src/Pm.Api
```

### 測試
```powershell
dotnet test                       # 後端全測試
cd src/Pm.Web ; npm test          # 前端(vitest + jsdom)
cd src/Pm.Web ; npm run e2e       # 瀏覽器 e2e(@playwright/test;webServer 自動起 app)
```

### 發版(自包含單檔 exe)
```powershell
cd src/Pm.Web ; npx ng build                              # 前端 → wwwroot
dotnet publish src/Pm.Api -p:PublishProfile=win-x64       # DirectML(預設)→ publish/(exe ~75MB + wwwroot)
# 另兩個推論 flavor(各為獨立 build,程式碼不動,僅換 profile):
dotnet publish src/Pm.Api -p:PublishProfile=win-x64-cuda       # CUDA(NVIDIA,24H2 以下)
dotnet publish src/Pm.Api -p:PublishProfile=win-x64-windowsml  # Windows ML(Win11 24H2+)
```
免裝 runtime、雙擊即跑。完整部署/設定/散布見 [`docs/deployment.md`](docs/deployment.md)。

---

## 資料放哪

| 東西 | 位置 | 備註 |
|---|---|---|
| 資料庫(唯一真相) | `pm.sqlite` | 備份＝複製此檔(可用 sqlite3 CLI `VACUUM INTO` 熱備);tag manifest 匯出為規劃中、app 尚未內建 |
| 縮圖快取 | `thumbs/`(依 hash 分桶) | 衍生資料,可重建;512px webp |
| WD14 模型 | `models/wd14`(opt-in 時下載) | 衍生資料,可重建 |
| 原始圖檔 | 你的來源資料夾 | **唯讀,絕不修改/搬移/改名,不寫 XMP** |

預設程式與資料分離(資料落 `%LOCALAPPDATA%`,Windows 正規做法);攜帶版可用 `Storage:BaseDir` 讓資料貼著 exe。

---

## 功能狀態

**Phase 1 核心可端對端運作**:掃描就地索引 → SHA-256 身分 → 縮圖 → 布林查詢 → Angular 相簿,前端各畫面已接真實 API。

- ✅ 掃描 / 對帳 / 布林查詢 / 路徑→tag / saved search / facet 樹 / 軟硬刪 / manual tag(皆有測試)
- ✅ 標籤庫管理:列表 / 改名 / 合併 / 刪除,正規化 + 全 Unicode 不分大小寫去重
- ✅ WD14 自動標籤端到端就緒(opt-in),DirectML 已實機驗證;tag 顯示層(中文顯示名 + 角色解析 + 來源徽章)
- ✅ 作品軸(copyright 拆分 + facet「作品→角色」DAG 樹)
- ✅ 資料夾路徑維度瀏覽 `/browse`(即時樹 + 麵包屑 + 下鑽 + 遞迴圖牆)
- ✅ async scan + SQLite 硬化、孤兒清理、單張重新處理 + 掃描自動痊癒
- ✅ AVIF 解碼支援(已測);HEIC / HEIF 解碼路徑就緒,真實樣本測試待補
- ✅ 前端 RWD:桌面縮放韌性 + 側欄/inspector 可收合 + 完整手機版抽屜式側板
- ✅ 交付:win-x64 自包含單檔 exe 已實測就緒(DirectML flavor)
- ✅ 內建 API explorer(開發用):OpenAPI 規格 `/openapi/v1.json` + Scalar 互動式文件 `/scalar/v1`
- ✅ 推論後端三 flavor:**DirectML**(預設,任何 DX12 GPU)/ **CUDA**(NVIDIA,24H2 以下)/ **Windows ML**(Win11 24H2+,EP 由 OS 動態下載)—— 編譯期經 `InferenceFlavor` 切套件,各有 publish profile + CI matrix。CPU / DirectML 已實機驗證;CUDA / Windows ML 已編譯與 publish 驗證,runtime 推論需對應硬體/OS(無 GPU 或非 24H2 環境無法在此驗)
- 🔲 Phase 2(規劃):CLIP image embedding 語意搜尋(sqlite-vec 或 Postgres+pgvector)

各子系統的設計與決策見 [`docs/design/`](docs/design/)。

---

## 專案結構

```
src/
  Pm.Api/       ASP.NET Core 宿主:DI、migration、API endpoints、serve wwwroot;TaggingWorker(背景服務)
  Pm.Scanner/   掃描 + 縮圖 + EXIF + 查詢 + tag closure/facet + 路徑→tag + TagService(service 層)
  Pm.Data/      EF Core:Entities/(九實體)、PmDbContext、Migrations/
  Pm.Ml/        推論後端抽象 IInferenceSessionFactory(三 flavor:DirectML 預設 / CUDA / Windows ML,編譯期切;無 GPU 退 CPU)+ WD14 pipeline
  Pm.Imaging/   AVIF/HEIC/HEIF 解碼橋接
  Pm.Web/       Angular(core / features / shell 分區;ng build 輸出至 ../Pm.Api/wwwroot)
tests/          與 src 一一對應的測試專案
docs/           design/(設計 spec)、deployment.md、mockups/
```

---

## 授權

[MIT](LICENSE) © 2026 alexlin7

第三方相依套件授權見 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
