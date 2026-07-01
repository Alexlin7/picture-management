---
status: active
last-reviewed: 2026-06-29
supersedes: []
superseded-by: []
related: []
---

# 設計文件

本目錄是專案的設計與決策記錄(spec)。每份文件記錄一個子系統或一次重要決策的「為什麼這樣做」;實作細節以程式碼與 git history 為準,本目錄只保留長期有價值的設計脈絡。

## 文件治理(權威 · 時序 · metadata)

文件多了之後,「找得到」(retrieval)與「該信哪份」(authority / currency)是兩件正交的事。本索引解決前者(導航),以下規則解決後者。

**1. 權威順序(內容衝突時誰贏)**

> `AGENTS.md` 鐵則 > 主設計 [`2026-06-21-picture-management-design.md`](2026-06-21-picture-management-design.md) §7 決策日誌 > 分項設計文件 > `README.md`

愈上層愈穩定、愈該信。下層與上層矛盾時,以上層為準,並回頭修正下層。

**2. canonical 歸屬(每個概念只有一個家,其餘連過去)**

| 內容類型 | canonical(唯一真相源) | 其他文件 |
|---|---|---|
| 不可違反的鐵則、開發約定 | `AGENTS.md` | 只引用,不重述規範 |
| 架構 / ER / DDL / 關鍵決策日誌 | 主設計 `2026-06-21-...` | 分項文件連過去 |
| 各子系統設計脈絡 | 對應分項設計文件 | —— |
| 功能現況 / 啟動方式(公開門面) | 根目錄 `README.md` | 人話摘要 + 連向上述,**非真相源** |

**3. 文件 ID 與路徑解耦**

文件 ID = 檔名去掉 `.md` 的 slug(例:`2026-06-24-async-scan-design`)。frontmatter 的 `related` / `supersedes` / `superseded-by` 一律寫 **ID**,不寫路徑;ID→路徑對照由本索引負責。搬檔/改名時更新本索引即可,交叉引用不必逐處改。

**4. frontmatter(機器可讀)**

每份設計文件頂端有 YAML frontmatter,只放會拿來判斷的欄位:

```yaml
status: active        # active / superseded / deprecated
last-reviewed: YYYY-MM-DD
supersedes: []        # 被本文取代的舊文件 ID
superseded-by: []     # 取代本文的新文件 ID
related: []           # 同子系統 / 有脈絡關聯的文件 ID
```

YAML = 文件**生命週期與關係**的權威層(機器可讀);頂部 prose(`日期 / 狀態 / 關聯`)= 人類細節與**實作進度**。兩者衝突時,以 YAML `status` 判定文件是否現行有效。

**5. 決策變更:supersede,不要默默覆寫或刪除**

決策改了 → ① 回寫 canonical(主設計 §7 或 `AGENTS.md`);② 舊文件加 `superseded-by`,新文件加 `supersedes`,**保留舊文件與其理由**(舊決策的「為什麼」常是最有價值的);③ 不直接刪除,避免 dangling reference 與重打同一場爭論。整份被取代才設 `status: superseded`;只有局部被改,在 canonical 補一條日誌即可。

## 從哪讀起

1. [`2026-06-21-picture-management-design.md`](2026-06-21-picture-management-design.md) — **主設計**:架構、ER/DDL、§7 決策日誌。先讀這份。
2. 啟動 / 功能現況 → 根目錄 [`README.md`](../../README.md);鐵則與開發約定 → 根目錄 [`AGENTS.md`](../../AGENTS.md)。
3. 動 UI 前 → [`2026-06-24-frontend-design-guidelines.md`](2026-06-24-frontend-design-guidelines.md)。
4. 發版 / 部署 → [`../deployment.md`](../deployment.md)。

## 索引

### 核心 / 資料層
- [`2026-06-21-picture-management-design.md`](2026-06-21-picture-management-design.md) — 主設計、ER/DDL、決策日誌。
- [`2026-06-30-system-dataflow-overview.md`](2026-06-30-system-dataflow-overview.md) — 系統資料流總覽(掃描 / 標籤 / 查詢三流 + 九表 + 語意搜尋層定位)導覽。
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
- [`2026-06-30-phase2-semantic-search-model-evaluation.md`](2026-06-30-phase2-semantic-search-model-evaluation.md) — Phase 2 語意搜尋模型選型評估(CLIP / DINOv2 / VLM、繁中、in-proc 約束)。

### 前端 / UI/UX
- [`2026-06-24-frontend-design-guidelines.md`](2026-06-24-frontend-design-guidelines.md) — 前端設計準則(樣式落點、token、a11y)。
- [`2026-06-24-ui-style-system-design.md`](2026-06-24-ui-style-system-design.md) — Tailwind v4 樣式系統地基。
- [`2026-06-24-gallery-topbar-ux-design.md`](2026-06-24-gallery-topbar-ux-design.md) — 相簿頂端操作 UX。
- [`2026-06-25-folder-browse-dimension-design.md`](2026-06-25-folder-browse-dimension-design.md) — 資料夾路徑維度瀏覽 `/browse`。
- [`2026-06-26-frontend-rwd-design.md`](2026-06-26-frontend-rwd-design.md) — 前端 RWD(桌面縮放韌性 + 側欄收合)。
- [`2026-06-28-mobile-drawers-design.md`](2026-06-28-mobile-drawers-design.md) — 完整手機版抽屜式側板。

### 測試
- [`2026-06-29-e2e-test-hardening.md`](2026-06-29-e2e-test-hardening.md) — e2e 測試壞味道審查 + 鐵則準則 + `@playwright/test` 遷移計畫。

### 稽核 / 品質
- [`2026-07-01-docs-code-audit.md`](2026-07-01-docs-code-audit.md) — 文件宣稱 ↔ 程式碼實作對照稽核快照(六子系統逐條核對 + 落差清單)。

### 前瞻 / 路線圖
- [`2026-06-28-nas-multiuser-scaling-roadmap.md`](2026-06-28-nas-multiuser-scaling-roadmap.md) — NAS / 多人擴展瓶頸地圖與分階段路線(非待辦)。
