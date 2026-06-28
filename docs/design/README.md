# 設計文件

本目錄是專案的設計與決策記錄(spec)。每份文件記錄一個子系統或一次重要決策的「為什麼這樣做」;實作細節以程式碼與 git history 為準,本目錄只保留長期有價值的設計脈絡。

## 從哪讀起

1. [`2026-06-21-picture-management-design.md`](2026-06-21-picture-management-design.md) — **主設計**:架構、ER/DDL、§7 決策日誌。先讀這份。
2. 啟動 / 功能現況 → 根目錄 [`README.md`](../../README.md);鐵則與開發約定 → 根目錄 [`AGENTS.md`](../../AGENTS.md)。
3. 動 UI 前 → [`2026-06-24-frontend-design-guidelines.md`](2026-06-24-frontend-design-guidelines.md)。
4. 發版 / 部署 → [`../deployment.md`](../deployment.md)。

## 索引

### 核心 / 資料層
- [`2026-06-21-picture-management-design.md`](2026-06-21-picture-management-design.md) — 主設計、ER/DDL、決策日誌。
- [`2026-06-22-scan-detection-design.md`](2026-06-22-scan-detection-design.md) — 掃描偵測策略邊界。
- [`2026-06-23-scanner-tagging-refactor-design.md`](2026-06-23-scanner-tagging-refactor-design.md) — 掃描重構 + tagging 解耦。
- [`2026-06-24-async-scan-design.md`](2026-06-24-async-scan-design.md) — async scan + scan-status 輪詢 + SQLite busy_timeout 硬化。
- [`2026-06-24-thumb-placeholder-and-autoscan-design.md`](2026-06-24-thumb-placeholder-and-autoscan-design.md) — 縮圖佔位 + 新增來源自動掃描。
- [`2026-06-25-logging-and-app-data-dir-design.md`](2026-06-25-logging-and-app-data-dir-design.md) — Serilog rolling file + app data dir 收斂。
- [`2026-06-25-orphan-photo-cleanup-design.md`](2026-06-25-orphan-photo-cleanup-design.md) — 孤兒 photo 清理維護端點。
- [`2026-06-26-photo-reprocess-and-scan-heal-design.md`](2026-06-26-photo-reprocess-and-scan-heal-design.md) — 單張重新處理 + 重掃自動痊癒。

### 標籤 / ML
- [`2026-06-22-tag-display-layer-design.md`](2026-06-22-tag-display-layer-design.md) — WD14 tag 顯示層(raw → 中文顯示名 + 角色解析)。
- [`2026-06-23-tag-display-v1-dataprep.md`](2026-06-23-tag-display-v1-dataprep.md) — 顯示層 v1 資料準備。
- [`2026-06-23-ml-layer-architecture-assessment.md`](2026-06-23-ml-layer-architecture-assessment.md) — `Pm.Ml` 推論層盤點(為 CLIP / GPU 自動偵測鋪路)。
- [`2026-06-25-tag-copyright-axis-design.md`](2026-06-25-tag-copyright-axis-design.md) — 作品軸(copyright 拆分 + `tag_relation` + facet 樹)。
- [`2026-06-25-second-tagger-cl-tagger-evaluation.md`](2026-06-25-second-tagger-cl-tagger-evaluation.md) — 第二 tagger(cl_tagger_v2)評估,deferred。

### 前端 / UI/UX
- [`2026-06-24-frontend-design-guidelines.md`](2026-06-24-frontend-design-guidelines.md) — 前端設計準則(樣式落點、token、a11y)。
- [`2026-06-24-ui-style-system-design.md`](2026-06-24-ui-style-system-design.md) — Tailwind v4 樣式系統地基。
- [`2026-06-24-gallery-topbar-ux-design.md`](2026-06-24-gallery-topbar-ux-design.md) — 相簿頂端操作 UX。
- [`2026-06-25-folder-browse-dimension-design.md`](2026-06-25-folder-browse-dimension-design.md) — 資料夾路徑維度瀏覽 `/browse`。
- [`2026-06-26-frontend-rwd-design.md`](2026-06-26-frontend-rwd-design.md) — 前端 RWD(桌面縮放韌性 + 側欄收合)。
- [`2026-06-28-mobile-drawers-design.md`](2026-06-28-mobile-drawers-design.md) — 完整手機版抽屜式側板。

### 前瞻 / 路線圖
- [`2026-06-28-nas-multiuser-scaling-roadmap.md`](2026-06-28-nas-multiuser-scaling-roadmap.md) — NAS / 多人擴展瓶頸地圖與分階段路線(非待辦)。
