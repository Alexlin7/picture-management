# Current Backlog Handoff

用途:給下一個 agent session 快速接手。已完成細節不要在這裡展開;現況看 `README.md`,鐵則看 `CLAUDE.md` / `agent.md`,完整背景看 `2026-06-21-picture-management-design.md`。

**最後更新:2026-06-24**(thumb 佔位+自動掃描、reconcile/inspector 共用 app-thumb、async scan + SQLite 硬化落地後)。

## 當前 backlog(尚未做,依建議順序)

1. **掃描器 / ML 後續**
   - GPU 廠牌自動偵測(目前 `InferenceBackendSelector` auto 傳 `gpuVendor=null`,固定 DirectML)。
   - `Pm.Ml` 整理為 CLIP 鋪路(來源:`2026-06-23-ml-layer-architecture-assessment.md`)。
   - 孤兒 photo 清理(2026-06-24 Codex async scan 後留下):防新孤兒已做(同 transaction);清舊孤兒=刪 DB,建議走**手動維護端點** +(可選)啟動時只 log 數量不自動刪。
   - per-root「重產縮圖」維護入口:重掃已會補缺縮圖,獨立 rebuild-thumbs 按鈕/端點屬 nice-to-have。

2. **圖牆 virtual scroll(真窗格化)**
   - 現況:已接無限捲(IntersectionObserver 哨兵),但 DOM 仍隨載入累積。
   - 下一步:評估 `@angular/cdk/scrolling` 或自製 windowing;masonry 不定高較麻煩,必要時改固定高度/欄位策略。

3. **小型前端體驗**
   - tag 合併時讓使用者選保留方向。
   - `/tags` 排序狀態持久化到 localStorage。
   - 批次 requeue 「依當前查詢 filter」scope(需新增後端 by-query scope;gallery-topbar Spec 3 ④ 已記為 deferred)。
   - 「重標全部」(`scope:{all:true}`,破壞性高)deferred,待使用者明示。
   - 響應式與手機版仍未設計;目前以桌面 workbench 為主。
   - a11y 尚未完整檢視(鍵盤操作、ARIA、焦點環)。
   - thumb 收尾 Minor(非阻塞):`thumb.spec.ts` 可補「重試空窗期間 skeleton 持續顯示」斷言;重試成功後 URL 殘留 `?r=n`(無害,僅 awareness)。

4. **可觀測性:log 套件 + log 落點(議題,待決策)**
   - **現況**:無 Serilog/NLog,只有 ASP.NET Core 預設 **console logging**(`appsettings.json` 只設 `Logging:LogLevel`,無 file sink)。打包成**單檔 self-contained exe(雙擊、無 console 視窗)後,log 直接消失** —— 出事(掃描錯誤、WD14 失敗、縮圖問題)沒有可追的紀錄。
   - **議題 ①(log 套件)**:選一個寫檔 + rolling 的 logging。選項:
     - **Serilog(建議)** —— .NET 生態最主流,`UseSerilog` 接 ASP.NET Core,rolling file + console sink、結構化、設定簡單。
     - NLog —— 同樣成熟,設定檔導向。
     - 內建 `Microsoft.Extensions.Logging` + 第三方 file provider —— 最輕,但功能少(無原生 rolling)。
   - **議題 ②(log 放哪)**:需要一個**每位使用者可寫、好找**的位置。建議 **`%LOCALAPPDATA%\sus-picture-management\logs\`**(rolling、保留 N 天/大小上限)。不要放 exe 旁(安裝版可能在 `Program Files` 唯讀)。
   - **相關(可一起或分開做)**:目前 `pm.sqlite`(`Data Source=pm.sqlite`)、縮圖 `thumbs`、模型 `models/wd14` 都**相對工作目錄**。單檔 exe 交付時這些也該一起落到 `%LOCALAPPDATA%\sus-picture-management\`(`data/` `thumbs/` `models/` `logs/`),否則雙擊執行的工作目錄不確定 → 資料散落。**log 落點建議與這個 app data dir 收斂一致。**
   - 接手:先決定 Serilog vs 其他 + 是否一併收斂 app data dir,再 brainstorming → spec。

5. **交付與 Phase 2**
   - 單檔 self-contained publish 尚待驗證(連帶上面 app data dir 落點)。
   - CUDA / Windows ML backend 仍是骨架(本 build 僅 cpu/directml)。
   - CLIP 語意搜尋未開始(Phase 2)。

## 已完成,不要重做

- **Phase 1 核心**:SQLite schema、掃描/對帳、縮圖、路徑→tag、查詢、facet、saved search、軟硬刪、manual tag、標籤庫。
- **Angular 前端**:gallery/import/reconcile/saved/roots/tags/inspector 已接真實 API;SPA 由 .NET serve。
- **WD14**:ONNX in-proc pipeline、TaggingWorker、opt-in host wiring、DirectML 實機驗證。
- **§B 掃描/Tagging 解耦(2026-06-23)**:快路徑重掃、chunk slow path、記憶體 set-diff 對帳、`enqueueTagging` 旗標、`/api/tag/requeue` + `/api/photos/{id}/retag`、`Inference:Wd14:*` 乾淨重命名。
- **WD14 tag 顯示層 v1(2026-06-22 spec)**:底線轉空白、表情對照、kind 分組、來源/信心徽章、character 括號解析(純前端)。
- **UI 樣式系統 Spec 1(2026-06-24)**:`@theme` token + a11y/motion + primitive 三態。
- **gallery-topbar UX Spec 3(2026-06-24,`2026-06-24-gallery-topbar-ux-design.md`)**:① 下拉驅動 substring 搜尋(運算子退場)、② 掃描鈕從 gallery 移除(改 roots 頁)、③ 收藏搜尋點卡套用(onPick→setTokens→導頁)、④ 「重標失敗」requeue 入口(by-query scope deferred)。
- **gallery 改進(2026-06-24)**:真實總命中數、WD14 待標佇列數(後端 count 端點)、搜尋狀態進 URL query(可重整/分享/上一頁)、密度切換接 masonry column-count、無限捲。
- **async scan + SQLite 硬化(2026-06-24)**:`POST /api/roots/{id}/scan` 回 202 + `GET .../scan-status` 輪詢(`RootScanCoordinator`);`SqliteBusyTimeoutInterceptor`(每連線 `PRAGMA busy_timeout` + connection string `Default Timeout`);掃描完整性(photo/location/job 同 transaction、縮圖補產不限新 photo、thumb endpoint `FileShare.ReadWrite` + 503 重試)。spec:`2026-06-24-async-scan-design.md`。
- **縮圖佔位 + 新增來源自動掃描(2026-06-24)**:共用 `<app-thumb>`(`@core/ui/thumb`)skeleton + 指數退避重試 + 失敗佔位,gallery/reconcile/inspector 皆已採用;`src` 對 photoId 變動有反應(修重用實例切圖卡 loading);新增來源後自動排掃描 + toast(不跳 confirm)。spec:`2026-06-24-thumb-placeholder-and-autoscan-design.md`、plan:`../plans/2026-06-24-thumb-placeholder-and-autoscan.md`(已完成,可移除)。

## 固定決策

- UI 行為庫用 Angular CDK;視覺維持自刻,不引整套 Material/PrimeNG。
- tag CI 去重靠 `name_ci`;顯示拼寫保留。
- tag kind 跨來源採語意升級不降級;標籤庫明示修改可覆寫。
- WD14 顯示清理只動前端 display model,不改 SQLite canonical tag。
- 掃描永遠純讀取與就地索引;不要引入會搬動原圖的投放夾模式。
- 新增來源後自動掃描(非破壞性、預期下一步),不用 confirm 對話框擋。
