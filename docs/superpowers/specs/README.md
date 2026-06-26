# Specs

設計文件(design / 決策記錄)。與 `plans/` 不同:**spec 完成後保留**(是設計與決策的長期記錄),不移除;只把過時的「狀態」行更新成已實作。

**最後整理:2026-06-26。**

## 新接手讀順序

1. 根目錄 `README.md`(現況/啟動)、`CLAUDE.md` / `agent.md`(鐵則)。
2. `2026-06-21-picture-management-design.md` — 主設計與決策日誌。
3. `2026-06-22-remaining-work-handoff.md` — 當前 backlog 與接手順序(**最後更新 2026-06-26**)。
4. 動 UI 前:`2026-06-24-frontend-design-guidelines.md`。
5. 要發版/部署:根目錄 `docs/deployment.md`(win-x64 自包含單檔 exe 操作指南)。

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
- `2026-06-24-gallery-topbar-ux-design.md` — 頂端操作 UX(Spec 3)✅。
- `2026-06-24-async-scan-design.md` — async scan + scan-status 輪詢 + SQLite busy_timeout 硬化 ✅。
- `2026-06-24-thumb-placeholder-and-autoscan-design.md` — 縮圖佔位 + 新增來源自動掃描 ✅。
- `2026-06-25-logging-and-app-data-dir-design.md` — Serilog rolling file + app data dir 收斂 ✅(log 級別後續再硬化:EF SQL 已壓 Warning,2026-06-25)。
- `2026-06-25-orphan-photo-cleanup-design.md` — 孤兒 photo 維護端點 + 啟動只 log ✅。
- `2026-06-25-tag-copyright-axis-design.md` — 作品軸(WD14 copyright 拆分 + tag_relation + facet 側欄)✅。
- `2026-06-25-folder-browse-dimension-design.md` — **資料夾路徑維度瀏覽 `/browse`(即時樹 + 麵包屑 + 子夾下鑽 + 遞迴圖牆 + 夾內疊 tag)✅(PR #5,2026-06-25)**。
- `2026-06-26-pr5-folder-browse-review-and-test-plan.md` — **PR #5 `/code-review high` 的 10 條 findings(F1–F10)落為可追蹤修復項 + 各層測試案例;全部驗證 + 修復 ✅(2026-06-26),並新增 Playwright 瀏覽器層 e2e infra**。

## 評估 / 參考(非待辦)

- `2026-06-23-ml-layer-architecture-assessment.md` — `Pm.Ml` 推論層盤點,為 CLIP / GPU 自動偵測(Phase 2)鋪路。
- `2026-06-25-second-tagger-cl-tagger-evaluation.md` — cl_tagger_v2 當第二 tagger(開關)評估;**deferred、低優先**。結論:非抽換 WD14、是新增 tagger 的中等重構(需抽 `ITagger` + pre/post/loader);動工前先確認授權(禁再配布)/速度/品質。

## 待決策(review,等使用者拍板)

- `2026-06-25-tag-sidebar-and-import-confirm-review.md` — 左側 tag facet 側欄 UX(① 分區整段收折 **已實作**;top-N / 側欄過濾 / 虛擬捲動 待)+ 匯入確認定位(② 資料夾維度已由 `/browse` folder-browse 回應;import-confirm 多 root 選擇器 / 自訂常用 tag preset 待決)。

## 待實作設計

目前無待實作 spec。backlog 項目(見 handoff)被接手時,先 `brainstorming` → 新 spec → `plans/` 的 plan,再實作。
