# Specs

設計文件(design / 決策記錄)。與 `plans/` 不同:**spec 完成後保留**(是設計與決策的長期記錄),不移除;只把過時的「狀態」行更新成已實作。

**最後整理:2026-06-24。**

## 新接手讀順序

1. 根目錄 `README.md`(現況/啟動)、`CLAUDE.md` / `agent.md`(鐵則)。
2. `2026-06-21-picture-management-design.md` — 主設計與決策日誌。
3. `2026-06-22-remaining-work-handoff.md` — 當前 backlog 與接手順序(**最後更新 2026-06-24**)。
4. 動 UI 前:`2026-06-24-frontend-design-guidelines.md`。

## 基礎 / 常讀

- `2026-06-21-picture-management-design.md` — 主設計、ER/DDL、§7 決策日誌。
- `2026-06-22-remaining-work-handoff.md` — backlog 真相文件(已完成 / 未做 / 固定決策)。
- `2026-06-24-frontend-design-guidelines.md` — 前端 design 準則(樣式落點、token、a11y)。

## 已實作(設計記錄,不要重做)

- `2026-06-22-scan-detection-design.md` — 偵測策略邊界;路線 A 已併入 scanner-refactor。
- `2026-06-22-tag-display-layer-design.md` — WD14 tag 顯示層 v1 ✅。
- `2026-06-23-scanner-tagging-refactor-design.md` — 掃描重構 + tagging 解耦 §B ✅(Slice 1a–4)。
- `2026-06-23-tag-display-v1-dataprep.md` — 顯示層 v1 資料準備 + Slice A/B ✅。
- `2026-06-24-ui-style-system-design.md` — UI 樣式系統地基(Spec 1)✅。
- `2026-06-24-gallery-topbar-ux-design.md` — 頂端操作 UX(Spec 3)✅(① 搜尋 ② 掃描鈕移除 ③ 收藏搜尋套用 ④ requeue 入口;by-query requeue scope deferred)。
- `2026-06-24-async-scan-design.md` — async scan + scan-status 輪詢 + SQLite busy_timeout 硬化 ✅(待補:孤兒 photo 清理、per-root rebuild-thumbs)。
- `2026-06-24-thumb-placeholder-and-autoscan-design.md` — 縮圖佔位(skeleton/重試/佔位)+ 新增來源自動掃描 ✅(gallery/reconcile/inspector 共用 `app-thumb`)。

## 評估 / 參考(非待辦)

- `2026-06-23-ml-layer-architecture-assessment.md` — `Pm.Ml` 推論層盤點,為 CLIP / GPU 自動偵測(Phase 2)鋪路;決定哪些現在抽、哪些等真實形狀再抽。

## 待實作設計

目前無待實作 spec。backlog 項目(見 handoff)在被接手時,先 `brainstorming` → 新 spec → `plans/` 的 plan,再實作。
