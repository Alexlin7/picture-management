# 前端 RWD 修復設計(桌面縮放韌性)

**日期**:2026-06-26
**狀態**:已實作(2026-06-27;桌面縮放韌性 + 側欄收合 + masonry 量測欄寬皆落地。手機 topbar/toolbar 為後續延伸,見下方非目標註)
**範圍**:前端 `src/Pm.Web`,Angular standalone + Tailwind v4 + 元件隔離 CSS
**前置**:本文件承接 2026-06-26 的 UI/UX 盤點 —— 既有前端只有 2 個有效斷點(1180 / 1500),圖牆欄數寫死、側欄/inspector 固定寬(58 + 252 + 350 = 660px 固定 chrome),視窗縮窄即破版、圖牆被擠到看不見。

## 1. 目標與非目標

### 目標
- **桌面縮放韌性**:視窗縮到約 700–800px 寬都不破版、圖牆永遠看得到。
- 圖牆欄數隨寬度**連續自適應**;固定寬側欄/inspector 在窄寬可收合(手動 + 自動)。
- 把散落各 `.css` 的寫死排版邏輯收斂成**可單測的 JS 量測驅動**。

### 非目標(YAGNI)
- **不做整頁手機版重排**(漢堡選單、抽屜式側欄、375px mobile-first 整頁重構)。使用者情境以桌面調整視窗為主。
  - **例外(2026-06-27 延伸,已完成):** gallery 頂部 `topbar` / `toolbar` 已補手機尺寸支援 —— 斷點 640px:topbar 折行 + 隱藏冗餘提示,toolbar 採「⋯ 更多」溢出選單收次要操作(模型佇列狀態 + 重標失敗);`rwd-resize-smoke.mjs` 補 480/375 寬度回歸。此為點狀修復,非整頁 mobile-first;抽屜式側欄、漢堡選單仍維持非目標。
- **不做 virtual scroll / 窗格化**(獨立 backlog,本次範圍外);本次只保證不破版 + 自適應。無限捲動沿用現有 IntersectionObserver。
- **不做 overlay peek**(收合側欄後懸浮預覽)。要看側欄就展開或拉寬視窗。

## 2. 根本改變:排版改由 JS 量測驅動

現況破版根因是排版邏輯寫死在 CSS(`column-count: 4`、`grid-template-columns: 252px 1fr 350px`、`@media (max-width: 1500px)`)。本次改為 **`ResizeObserver` 量測容器實際寬度** 來決定欄數與側欄收合,CSS media query 退為輔助。

斷點集中成 TS 常數(單一真相源,可單測),不再散落各 `.css`:

| 常數 | 值 | 意義 |
|---|---|---|
| `INSPECTOR_COLLAPSE` | `1180` | stage 寬 < 此 → inspector 自動收 |
| `FACET_COLLAPSE` | `940` | stage 寬 < 此 → facet 側欄 / 資料夾樹 自動收 |
| masonry `minColWidth` | 見 §3 | dense 150 / 標準 180 / large 280 |

> 註:CSS `@media` 不能吃 `var()`,故斷點放 TS 常數而非 CSS 變數;少數仍需的 CSS 微調用字面量並在註解標明來源於本表。

## 3. 共用瀑布流元件 `<app-masonry>`(核心)

新增 standalone 元件,**photo-grid 與 browse-grid 共用**,取代兩份重複的 `column-count` CSS。

### 介面
```
<app-masonry [items]="photos()" [aspect]="aspectFn" [minColWidth]="180" [gap]="12">
  <ng-template let-item> ...自己的 tile markup... </ng-template>
</app-masonry>
```
- `items: T[]` — 資料陣列。
- `aspect: (item: T) => number` — 回傳 `w/h` 長寬比。資料已現成(`photo-grid` 的 `aspect(p)`)。
- `minColWidth: number` — 最小欄寬;三種檢視模式各給一組(dense 150 / 標準 180 / large 280),取代寫死的 5/4/2 欄。
- `gap: number` — 欄/列間距(預設 12,對齊現況)。
- 內容投影 `<ng-template>` — photo-grid / browse-grid 各自保留 tile markup、badge、選取/hover 樣式。

### 演算法
- **欄數** = `max(1, floor((containerW + gap) / (minColWidth + gap)))` → 最少 1 欄,**永不破版**。
- **欄寬** = `(containerW - gap*(cols-1)) / cols`。
- **每格高度** = `欄寬 / aspect(item)` —— 用已知長寬比直接算,**不等圖載入、不量 DOM**(避免 layout thrash)。
- **貪婪塞最矮欄** → 真瀑布流外觀。
- **定位**:每格 wrapper 用 `position: absolute; left; top; width`(**不用 transform 定位**)。容器高度 = 最高欄高,由 JS 設。
- **重算時機**:`ResizeObserver` 容器寬變化(以 `requestAnimationFrame` debounce);`items` 變更(無限捲動 append)時重算。

### 與既有互動的相容
- **hover 上浮**:現況 `.tile:hover { transform: translateY(-3px) }` 會與定位 transform 衝突 —— 故定位改用 `left/top`,transform 純留給 hover/選取的縮放上浮(掛在 tile 內層或 tile 自身皆可,因定位不佔用 transform)。
- **無限捲動**:sentinel + IntersectionObserver 沿用;append 後重算版面、延長容器高度。
- **未來 virtual scroll 的加分**:座標已由 JS 算好,日後窗格化只需依捲動位置裁切渲染範圍即可,本設計不實作但不擋路。

## 4. 可收合側欄機制

新增共用 util `useStageWidth(hostRef)`:回傳 stage 容器寬度的 signal(內部掛 `ResizeObserver`,rAF debounce)。各 view 用它驅動收合。

### 行為
- facet 側欄 / inspector / 資料夾樹側欄 各有 `collapsed` signal + 一顆 toggle 鈕(**隨時可手動折疊/展開**)。
- **自動收**:`effect()` 監看 stage 寬,跨過 §2 門檻時自動收(inspector 先收、再來 facet);回寬時自動展開,**除非使用者手動收過**(`userOverride` 旗標記住使用者意圖,避免自動展開覆蓋手動收合)。
- 收合後對應欄寬變 0:`gallery-view` / `browse-view` 的 `grid-template-columns` 改成**依 collapsed 狀態動態算**(綁定 inline style 或 class)。圖牆立即吃滿剩餘空間 → 解決「看不到東西」。
- 收合後留一條細邊/箭頭可手動展開。

### 動態欄寬範例(gallery-view)
- 兩側皆展開:`[facetW] 1fr [inspectorW]`
- inspector 收:`[facetW] 1fr 0`
- 兩側皆收:`0 1fr 0`(圖牆全寬)

## 5. 雜項破版點修正

- `gallery/photo-grid/photo-grid.css` `.ac-pop { min-width: 240px }` → `min-width: min(240px, 100%)`,窄寬不溢出。
- `browse/inner-tag-filter/inner-tag-filter.css` `.ac-pop { min-width: 210px }` → 同上處理。
- `photo-grid.css` 搜尋框 `max-width: 640px` **保留**(只是上限,不破版)。
- shell activity bar `58px` **保留**(夠窄、不影響)。
- manage 五頁(roots / tags / import-confirm / reconcile / saved-searches):皆為 `max-width` 置中單欄,會自己縮 → **本次僅快速回歸確認**,不主動重排;順手檢查 `tag-manager` 的 `grid-template-columns: 26px minmax(0,1fr) 110px 72px 60px auto` row 在窄寬無 overflow(`minmax(0,1fr)` 已保護,預期 OK)。

## 6. 驗證

擴充現有 Playwright e2e(仿 `src/Pm.Web/e2e/browse-smoke.mjs`),新增 `e2e/rwd-resize-smoke.mjs`:

載入 gallery → 把 viewport 依序縮到 **1400 / 1100 / 820 / 720** px,每段斷言:
1. `document.documentElement.scrollWidth ≤ clientWidth`(無橫向破版)
2. 圖牆 tile 可見(數量 > 0 且在視口內)
3. 欄數確實隨寬度遞減(讀 masonry 算出的欄數或 tile 排列)
4. inspector 在 < 1180 自動收、facet 在 < 940 自動收

browse 頁做對應的縮版抽查(至少斷言無橫向破版 + tile 可見)。

## 7. 改動檔案清單

| 檔案 | 動作 |
|---|---|
| `src/Pm.Web/src/app/gallery/masonry/masonry.ts`(新,放共用位置) | JS 瀑布流共用元件 |
| `src/Pm.Web/src/app/core/use-stage-width.ts`(新) | ResizeObserver 寬度 signal util |
| `src/Pm.Web/src/app/core/layout-breakpoints.ts`(新) | 斷點 TS 常數 |
| `features/gallery/photo-grid/photo-grid.{ts,html,css}` | 接 `<app-masonry>`、移除 column-count |
| `features/browse/browse-grid/browse-grid.{ts,html,css}` | 同上 |
| `features/gallery/gallery-view/gallery-view.ts` | 動態 grid 欄寬 + 收合狀態 |
| `features/browse/browse-view/browse-view.ts` | 同上(資料夾樹側欄) |
| `features/gallery/facet-sidebar/facet-sidebar.{ts,css}` | toggle 鈕 + 收合寬度 |
| `features/inspector/inspector/inspector.{ts,css}` | 同上 |
| `features/browse/folder-tree-sidebar/folder-tree-sidebar.{ts,css}` | 同上 |
| `features/gallery/photo-grid/photo-grid.css` + `features/browse/inner-tag-filter/inner-tag-filter.css` | `.ac-pop` min-width 修正 |
| `src/Pm.Web/e2e/rwd-resize-smoke.mjs`(新) | resize 回歸 e2e |

> 共用元件落點(`gallery/masonry` vs `core/`)實作時依現有目錄慣例定案;原則:masonry 與 thumb 同層、`use-stage-width` 與斷點常數進 `core/`。

## 8. 樣式慣例遵循(CLAUDE.md)

- 元件 `.css` 為隔離編譯,**不得** `@apply`/`@tailwind`/`@reference`;一律手寫 + `var(--token)`。
- 顏色/圓角/陰影走既有 token,不寫裸 hex。
- 新增 toggle 鈕沿用全域 `:focus-visible` ring,不 `outline:none`。
- 收合動畫尊重 `prefers-reduced-motion`(全域已降載)。

## 9. 風險與取捨

- **失去 CSS columns 的零 JS**:改 JS masonry 後,版面在極端快速 resize 時靠 rAF debounce 平滑;以 aspect 直接算高,無 reflow 讀寫迴圈,風險低。
- **aspect 缺失**:若某筆資料無長寬比,以預設 1:1 fallback,不讓版面崩。
- **小幅回歸面**:photo-grid / browse-grid 的 tile markup 移進 `<ng-template>`,需確認 badge/hover/選取樣式在投影後仍正確(e2e + 手測涵蓋)。
