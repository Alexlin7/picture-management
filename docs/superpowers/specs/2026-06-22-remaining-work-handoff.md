# 剩餘工作 Handoff(2026-06-22)

> **用途:** 這份是給「下次開 agent session」的接手文件。彙整目前未完成的工作、優先序、相關檔案與已定案決策,讓接手者不必重新摸索、也不要重問已決定的事。
> **先讀:** 根 [`README.md`](../../../README.md)(現況/啟動)、[`CLAUDE.md`](../../../CLAUDE.md)(鐵則)、本資料夾 `2026-06-21-picture-management-design.md`(完整設計 + §7 決策日誌)。

---

## 1. 近期已完成(feat/wd14-worker 分支,2026-06-22)

- **Code review 修正一輪**:TaggingWorker 啟動回收孤兒 `running`、tag CI 去重改 `name_ci`(全 Unicode)、kind 語意升級不降級、模型下載 atomic rename、SQLite WAL、combobox 競態防護、空白名 400、ListAsync limit 推 SQL、tagColor 統一、worker/manual 共用 `AttachTag` 消 N+1、Wd14Setup backend 收斂。
- **標籤庫 CRUD 補齊**:後端 `POST /api/tags`(建純標籤)、`PUT` 改 kind(`TagService.UpdateAsync`);前端 `/tags` 頁新增/改名/改 kind/排序切換/顯式合併/批次刪除。
- **UI 基建**:`@angular/cdk` 的 toast(`core/ui/toast.ts`)+ confirm(`core/ui/confirm.ts`),取代所有原生 `alert/confirm/prompt`。
- **facet 點選加搜尋 token** + **搜尋框 autocomplete**;**roots 新增來源改 inline 表單**。

後端全測試綠(Ml 19 / Data 7 / Scanner 47 / Api 21 = 94)。前端 `ng build` 通過,**但前端無自動化測試,需手測**。

---

## 2. 待辦(依優先序)

### A. 重要 — 效能與架構

1. **圖牆 virtual scroll**(`@angular/cdk/scrolling`)
   - **現狀:** `photo-grid` 的 masonry 一次渲染所有已載入 tile(`@for (p of photos())`),十萬量級會嚴重卡頓。目前靠 keyset 無限捲分頁(每頁 60)累積,但 DOM 節點只增不減。
   - **要做:** 導入 `cdk-virtual-scroll-viewport`(或自製 windowing)只渲染可視範圍。masonry(不定高)用 virtual scroll 較棘手 —— 評估改固定欄寬網格 + `itemSize`,或用 masonry-friendly 的 windowing。
   - **檔案:** `features/gallery/photo-grid/photo-grid.{ts,html,css}`、`gallery.store.ts`(loadMore)。

2. **combobox 抽共用元件**
   - **現狀:** 三處幾乎相同的「debounce 查 `api.tags` → 下拉 → ↑↓/Enter/Esc + 過期防護」:`inspector`(加標籤到圖)、`photo-grid`(加搜尋 token)、標籤庫間接相關。各自一份。
   - **要做:** 抽一個 `core/ui/tag-combobox`(或 `TagSuggest` signal helper),封裝查詢 + 鍵盤 + 浮層,呼叫端只給「選中時做什麼」callback。注意三者差異:inspector 有「建立新標籤」列、photo-grid 有 `-排除` 語法 fallback。
   - **檔案:** `features/inspector/inspector/inspector.{ts,html}`、`inspector.store.ts`、`features/gallery/photo-grid/photo-grid.{ts,html}`。
   - **注意:** inspector 的 combobox 剛重構過(comboRows 列模型化),抽共用時別弄壞鍵盤導航;前端無測試,動完務必手測。

### B. 小改進

3. **合併讓使用者選保留方向** — 目前 `tag-manager.mergeSelected` 自動「少併入多」。可加 dialog 選「保留 A / 保留 B」。檔案 `features/tags/tag-manager/tag-manager.ts`。
4. **排序持久化** — `/tags` 排序選擇重整後重置;存 localStorage。檔案 `features/tags/tags.store.ts`。
5. **photo-grid 死鈕接線** — topbar 的「儲存搜尋」「掃描」鈕目前無作用(README 已標 deferred)。「儲存搜尋」可接 `PmApi.createSavedSearch`;「掃描」需選來源。
6. **總命中數 / WD14 佇列數** — 目前相簿顯示「已載入數」、WD14 佇列固定 0(`gallery.store` 註明 deferred)。需後端加 count 端點(search 回總數、`tagging_job` pending 計數)。

### C. WD14 / 推論(Phase 2,使用者明示先擱置)

7. **WD14 opt-in 實機驗證** — `Inference:Enabled=true` 首次標註會 HF 下載 ~300MB 模型 + `selected_tags.csv`;需在真實圖庫跑一遍,校正 category↔kind 對應與門檻(`Wd14Postprocess` 註明待校正)。
8. **WD14 失敗 job 退避重試** — 目前失敗只標 `error` + `Attempts++`,不自動重排(崩潰卡 `running` 的會啟動回收,但 `error` 的不會)。
9. **檢視器 WD14 建議 UI** — 推論結果的 ✓採用/✕拒絕 chip(設計 spec §6 有,目前移除)。
10. **CUDA / Windows ML backend** — `Pm.Ml` 僅骨架;本 build 僅 cpu/directml。CUDA 需專屬 publish profile;WinML 待 Win11 24H2 + App SDK。

### D. 其他(README 🔲)

11. **單檔自包含 exe 交付** — `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` 設定與驗證。
12. **Phase 2 — CLIP 語意搜尋** — image embedding → 向量查詢(sqlite-vec 或遷 Postgres+pgvector)。未開始。

---

## 3. 待手測清單(前端無自動測試;`dotnet run --project src/Pm.Api` 開 localhost:5180)

- **標籤庫 `/tags`:** 新增(含撞既有名提示)、改名、改 kind、排序切換(名稱/類別/使用數/建立 升降)、選 2 個合併、批次刪除、單筆刪除(confirm dialog)。
- **facet 側欄:** 點各層標籤(作品/角色/屬性/年份/無上層)是否加 token;展開箭頭仍只展開不加 token。
- **搜尋框:** 打字下拉既有標籤、↑↓選、Enter/點選加 token、Esc 收、`-排除` 語法 fallback。
- **共通:** toast(右上,成功/錯誤)、confirm(Esc / 點背景 / 焦點鎖)、roots 新增來源 inline 表單。

---

## 4. 已定案決策(別重問)

- **UI 行為 lib = `@angular/cdk`**(已在 deps),只拿行為(Overlay/Dialog/a11y/未來 drag-drop/scrolling),**視覺自刻**,不引整套 Material/PrimeNG。
- **標籤庫排序 = 排序切換**(非手動拖曳;手拖對上萬標籤不實際、語意模糊)。
- **tag.kind 跨來源 = 語意升級不降級**(character/copyright/meta > general > manual/path);但標籤庫**明示**改 kind 直接覆寫(`UpdateAsync`,允許降級)。
- **tag CI 去重 = `name_ci`**(`ToLowerInvariant`,`SaveChanges` 自動維護)+ 唯一索引;非 ASCII 也去重。
- **新增標籤參數:** 必填 `name`(正規化 + CI 去重)、選填 `kind`(預設 `manual`);`id`/`name_ci`/`count` 系統自動帶。撞既有 CI → `POST` 回 `200 existed:true`。
- **WD14 = Phase 2 擱置**(使用者明示),先不碰標籤建議 / 佇列數 / 實機驗證。

---

## 5. 接手起步建議

下次 agent 從 **A1(virtual scroll)** 或 **A2(combobox 抽共用)** 開始最有價值。動前端務必:改完 `ng build` + 起 app 手測(無自動測試保護)。動後端走 TDD(既有測試是保護網)。設計決策有變更時,同步更新 `2026-06-21-picture-management-design.md`(§4/§7)與 README/CLAUDE(CLAUDE.md 鐵則要求)。
