# Current Backlog Handoff

用途:給下一個 agent session 快速接手。已完成的 Phase 1 細節不要在這裡展開;現況看 `README.md`,鐵則看 `CLAUDE.md` / `agent.md`,完整背景看 `2026-06-21-picture-management-design.md`。

## 優先序

1. **掃描效能 + tagging 解耦**
   - 來源 spec:`2026-06-23-scanner-tagging-refactor-design.md`。
   - 現況:Slice 1a 已完成並提交:
     - `c5168bc Optimize scanner fast-path rescan`
     - `450f7fd Tighten scanner fast-path tracking`
   - 已修:重掃快路徑開掃先載入 `photo_location` + `photo.file_size` dict,未變檔只批次更新 `LastSeenAt`;不再 `Include(Photo)`,避免 `Photo.Exif` 與十萬級 `Photo` entity 進 change tracker。Scanner 48 / 全測試 95 綠。
   - Slice 1b 已完成:初次匯入/大量新檔改 chunk slow path,批次查 photo by hash、同批 hash 去重、兩階段批次新增 photo/location/job;並已補上批次後 detach slow-path entities,避免初次匯入 change tracker 無界成長。Scanner 51 綠。
   - Slice 1c 已完成:**實機證實** EF Core 10 + SQLite 對 `!seenPaths.Contains` 是逐元素 `NOT IN (@p1...@pN)`,>32766 撞 `'too many SQL variables'`(十萬圖庫對帳必崩)。改用記憶體 set-diff(重用 `locationsByPath`)+ 以 location id `Chunk(10_000)` 分塊 `ExecuteUpdate`,無 schema 變更。
   - Slice 2 已完成:`ScanRootAsync(rootId, enqueueTagging=true, ...)` + job 排入包 `if(enqueueTagging)`(縮圖照產);端點 `?enqueueTagging=` 未帶則跟隨 `Inference:Enabled`(關→純索引不堆死 job),明示可覆寫(`true`=pre-queue)。能力關時前端自動排程 toggle 反灰之 UI 約定已寫進 spec §B.1/§D。Scanner 54 / Api 23 / 全測試 103 綠。
   - Slice 3 已完成:`POST /api/tag/requeue` + `POST /api/photos/{id}/retag`,支援 `retry` / `refresh` / `clear` 與四選一 scope(`photoIds` / `error` / `root` / `all`);job upsert、refresh/clear 只清該批 `source='wd14'` tag、root/all 只挑 present、大量 IN 分塊。
   - Slice 4 已完成:`Inference:Enabled`/`Backend` **乾淨重命名**為 `Inference:Wd14:Enabled`/`Backend`(無 fallback);3 prod 讀取點(`Wd14Setup` gate+backend、scan enqueue 預設)+ `appsettings.json` + launchSettings + 測試同步遷移。全測試 110 綠。**§B 掃描/Tagging 解耦至此全部完成。**
   - 下一步:§B 完。後續候選見下方架構盤點(GPU 自動偵測、Pm.Ml 整理為 CLIP 鋪路)或回到前端 backlog。

2. **WD14 tag 顯示層清理**
   - 來源 spec:`2026-06-22-tag-display-layer-design.md`。
   - 範圍:純前端顯示層,canonical tag 不動。做底線轉空白、表情對照表、kind 分組、來源/信心度徽章、character 括號解析。
   - 注意:若同時抽 tag combobox 共用元件,先抽共用,再疊顯示層。

3. **圖牆 virtual scroll**
   - 現況:gallery 已接真實 API 與 keyset 載入,但 DOM 仍會隨載入累積。
   - 下一步:評估 `@angular/cdk/scrolling` 或自製 windowing。masonry 不定高較麻煩,必要時改固定高度/固定欄位策略。

4. **小型前端體驗**
   - tag 合併時讓使用者選保留方向。
   - `/tags` 排序狀態持久化到 localStorage。
   - photo-grid 的「儲存搜尋」「掃描」按鈕補接線。
   - 搜尋總命中數與 WD14 pending/error 計數需要後端 count 端點。
   - 搜尋狀態進 URL query,讓 facet/token 查詢可分享、重整後可復原。
   - 響應式與手機版仍未設計;目前以桌面 workbench 為主。
   - a11y 尚未完整檢視,包含鍵盤操作、ARIA 與焦點環。

5. **交付與 Phase 2**
   - 單檔 self-contained publish 尚待驗證。
   - CUDA / Windows ML backend 仍是骨架。
   - CLIP 語意搜尋未開始。

## 已完成,不要重做

- Phase 1 核心:SQLite schema、掃描/對帳、縮圖、路徑到 tag、查詢、facet、saved search、軟硬刪、manual tag、標籤庫。
- Angular 前端:gallery/import/reconcile/saved/roots/tags/inspector 已接真實 API;SPA 由 .NET serve。
- 標籤一條龍:TagService 去重、標籤庫管理頁、inspector combobox 都已完成。
- WD14:ONNX in-proc pipeline、TaggingWorker、opt-in host wiring、DirectML 實機驗證已完成。

## 固定決策

- UI 行為庫用 Angular CDK;視覺維持自刻,不引整套 Material/PrimeNG。
- tag CI 去重靠 `name_ci`;顯示拼寫保留。
- tag kind 跨來源採語意升級不降級;標籤庫明示修改可覆寫。
- WD14 顯示清理只動前端 display model,不改 SQLite canonical tag。
- 掃描永遠純讀取與就地索引;不要引入會搬動原圖的投放夾模式。
