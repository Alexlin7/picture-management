# Current Backlog Handoff

用途:給下一個 agent session 快速接手。已完成的 Phase 1 細節不要在這裡展開;現況看 `README.md`,鐵則看 `CLAUDE.md` / `agent.md`,完整背景看 `2026-06-21-picture-management-design.md`。

## 優先序

1. **掃描效能 + tagging 解耦**
   - 來源 spec:`2026-06-23-scanner-tagging-refactor-design.md`。
   - 現況:`LibraryScanner.ScanRootAsync` 仍逐檔查 `photo_location` 並多次 `SaveChanges`;新 photo 也在掃描流程直接排 `tagging_job`。
   - 下一步:先做切片 1「批次載入消 N+1 + 批次 commit」,再做掃描排 job 可選、requeue 端點、WD14/CLIP 能力開關拆分。

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
