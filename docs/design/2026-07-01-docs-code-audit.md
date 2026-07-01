---
status: active
last-reviewed: 2026-07-01
supersedes: []
superseded-by: []
related: [2026-06-21-picture-management-design, 2026-06-26-frontend-rwd-design, 2026-06-29-e2e-test-hardening, 2026-06-23-scanner-tagging-refactor-design]
---

# 文件宣稱 ↔ 程式碼實作 對照稽核(2026-07-01)

- 日期:2026-07-01
- 狀態:**稽核快照(非設計決策、非待辦)**。記錄某一時點「文件說有」與「程式碼真有」的對照結果,供後續回寫 canonical 文件與補測試取用。落差的**修正動作另行提案**,本檔只陳述現況。
- 方法:六個子系統(`Pm.Data` / `Pm.Scanner` / `Pm.Ml` / `Pm.Imaging` / `Pm.Api` / `Pm.Web`)各派一支稽核 agent,先讀 `README.md` / `AGENTS.md` / `CHANGELOG.md` 與相關分項設計文件,再打開對應程式碼與測試逐條核對;每條宣稱給 `matched / partial / mismatch / missing / undocumented` 判定 + `file:line` 佐證。**只讀不改。**

## 核心結論

核心宣稱**幾乎全部對得上實作且有測試覆蓋**,**無任一鐵則被實質違反**。落差集中在三類:①文件措辭與現況不精確;②規劃中的能力被寫得像已完成;③少數測試/樣式小 gap。以下逐項。

## 落差彙總(7 項 + undocumented 清單)

| # | 子系統 | 類型 | 落差 | 佐證 |
|---|---|---|---|---|
| 1 | Pm.Data / Pm.Scanner | mismatch | 鐵則 #10 稱「測試連線字串**亦流經** `SqliteSetup.BuildConnectionString`」,但 `Pm.Data.Tests`、`Pm.Scanner.Tests` 皆手刻 `Data Source=…;Foreign Keys=True`,且結構上不參照 `Pm.Api`(避免循環相依)無法呼叫該函式。實際僅 `Pm.Api.Tests` 流經。行為等價、**無安全風險**,但日後 `BuildConnectionString` 若加新 pragma,這兩個測試專案不會跟上 | `SqliteSetup.cs:22-31` vs `Pm.Data.Tests/SchemaTests.cs:12`、`Pm.Scanner.Tests`(13 檔) |
| 2 | Pm.Data | **missing** | 鐵則 #3 + `README.md:103` 稱「備份＝複製 .sqlite(**VACUUM INTO 熱備**)+ **tag manifest 匯出**(hash,tag 獨立檔)」,但全 repo 無 `VACUUM`/`manifest`/`backup` 實作。主設計 §12 亦自承 manifest 格式「待動工才明朗」。目前只能理解為「使用者自行以 sqlite3 CLI 操作」,README 未註明「app 未內建」易誤導 | 全 repo grep 無命中 |
| 3 | Pm.Ml | mismatch | `AGENTS.md` / 架構總覽稱 tagging_job 是「`Channels`+`BackgroundService`」,但 `TaggingWorker` 實際是純 DB 輪詢(`while` + `Task.Delay(2s)`),無 `System.Threading.Channels` 實際呼叫(僅傳遞性相依)。**非鐵則違反**(鐵則 #7 只講「DB-backed 佇列」),但架構圖措辭與實作不符 | `TaggingWorker.cs:21-29` |
| 4 | Pm.Imaging | partial | `README:122`「✅ AVIF / HEIC / HEIF」三格式並列打勾暗示皆驗證,但測試僅完整覆蓋 **AVIF**;HEIC 只測 mime 映射且以 AVIF 內容偽裝(Magick-Q8 無 HEIC 編碼);HEIF 副檔名**零測試** | `ImageLoaderTests.cs:37-85` |
| 5 | Pm.Api | partial | `AGENTS.md`「設定分層」稱「行為層走 `app_setting`」,但全 repo 無 app_setting 表/實體/端點。設計文件本身註明是規劃中(`scanner-tagging-refactor-design.md:130-138`),AGENTS 口吻讀來像既成慣例 | grep 無 app_setting |
| 6 | Pm.Web | partial | 2 處**裸 hex 殘留**未 token 化(違前端鐵則「顏色一律走 token」)| `photo-grid.css:386 #cfd3da`、`shell.css:41 #101216`(shell logo conic-gradient 為文件已記錄之已知 gap) |
| 7 | Pm.Web | **undocumented** | masonry **已實作 virtual scroll / windowing**(scrollEl/overscan/visibleItems),但 RWD 設計文件 §1 明列此為**非目標**,未回寫決策變更 —— 違反文件治理鐵則「改設計決策要回寫 canonical」 | `masonry.ts:52-97` |

### 反向落差(程式碼超前文件)

- **e2e 遷移已完成**:5 支 `.mjs` 煙霧腳本已**全數轉為 7 支 `.spec.ts` 並刪除**,`playwright.config.ts`(`baseURL` / `webServer` / `testIdAttribute`)齊備,e2e 鐵則零違反(`page.$` / `force:true` / `expect(await` / 硬編 localhost 全零命中)。但 `2026-06-29-e2e-test-hardening.md` / CHANGELOG 仍寫「既有 5 支待分階段遷移」。文件落後於程式,應更新進度。

### undocumented(程式有、文件未收錄;可選補文件)

- **Pm.Api**:OpenAPI + Scalar 互動式 explorer(`/openapi/v1.json`、`/scalar/v1`)README 全無提及;maintenance 端點實際路徑(`/api/maintenance/orphan-photos`、`/api/maintenance/copyright-axis/rebuild`)未列。
- **Pm.Ml**:`CudaSessionFactory` / `WindowsMlSessionFactory` **無單元測試**(受 `#if` 編譯條件限制,僅靠 CI build+publish 驗證),README/CHANGELOG 未言明;`WindowsMlSessionFactory.SelectedProvider` 可觀測欄位未提。
- **Pm.Scanner**:`PathTagService` 對 `TagId=null` 舊規則的「自我修復」補建邏輯(`PathTagService.cs:93-116`)。
- **Pm.Imaging**:`.jfif` 納入掃描白名單(由 ImageSharp 原生解),文件只提三格式。

## 全 matched 的核心(佐證從略,詳見各 agent 原始輸出)

九表 / 身分位置分離(photo ↔ photo_location)/ FK cascade 設定源 + 全關聯 `OnDelete(Cascade)` / `photo_tag.source`+`confidence` / 掃描就地索引 · SHA-256 身分 · 搬移偵測 · 512px webp 縮圖 · EXIF / 布林多軸查詢(AND/排除)· DAG 標籤閉包(recursive CTE)· facet 樹 / 路徑→tag 匯入後確認 / 標籤庫管理 + 全 Unicode CI 去重 / 作品軸(copyright 拆分 + DAG)/ browse 後端(即時樹 + 遞迴計數)/ 三 flavor 編譯期切(`InferenceFlavor` + `INFER_*` + 三 publish profile + CI matrix)/ `InitializeAsync` 暖機接縫 / `CudaSessionFactory` + 真正的 `WindowsMlSessionFactory`(三層 EP 降級)/ WD14 pipeline(448·BGR·NHWC·門檻·opt-in 下載)/ `ModelArtifactDownloader` 抽出 / 原檔唯讀(唯讀串流、無寫回)/ API localhost-only 無認證 / 軟刪 archive + 硬刪 purge cascade / serve wwwroot + SPA fallback / `%LOCALAPPDATA%` + `Storage:BaseDir` / 前端 API 串接 · RWD · token 系統(P1)· a11y · `.input` primitive · `::ng-deep` 已全清。

## 建議後續(需另行提案,計畫先行)

1. **回寫措辭**(#1 #3 #7 + e2e 反向落差):修 `AGENTS.md` 鐵則 #10 與架構圖用詞、RWD 文件回寫 virtual scroll 解禁、更新 e2e 遷移進度。
2. **標「規劃中/需手動」**(#2 #5):README 備份段與 AGENTS 設定分層段加註,避免讀者誤以為既成功能。
3. **補測試/樣式**(#4 #6):真實 HEIC/HEIF fixture 補測;2 行裸 hex token 化。
4. **可選補文件**(undocumented 清單):OpenAPI/Scalar、Cuda/WindowsMl 無測試現況等擇要補入 README / deployment。
