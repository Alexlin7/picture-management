# ③g 完整手機版 — 抽屜式側板設計

**狀態:設計定案,待實作(2026-06-28)。** 對應 backlog `2026-06-22-remaining-work-handoff.md` 的 ③g。
取代先前撤掉的「窄寬 inspector 覆蓋層」半成品(那版 close X 浮在內容上、疊住 ⤢ 放大鈕 —— 本設計以「抽屜自帶 header 關閉鈕」根治)。

可點 mockup:`docs/mockups/mobile-drawers-preview.html`(瀏覽器開,下方有狀態切換鈕)。

## 目標與範圍

- **目標情境:兩者都要** —— 桌面視窗縮窄不破版 + 真手機/平板觸控可用(未來 NAS 手機連)。
- **核心問題:** 桌面三欄(rail + facet/tree + grid + inspector)在窄寬硬塞會把圖牆擠爆、浮動關閉鈕疊住內容按鈕。一塊塊補會打地鼠。
- **解法:** 窄寬時兩個側板(facet/tree、inspector)改成**共用一支 `DrawerPanel`** 的覆蓋式抽屜,一次解決、不再各別補。

### 非目標
- 不改桌面(≥ 斷點)版面行為 —— 維持現有三欄 + edge 箭頭收合。
- 不改 rail 導覽形式(不做 bottom-nav / 漢堡);rail 保留。
- 不動後端、資料模型、查詢邏輯。
- 不重寫 facet / inspector 內容本身 —— 原元件原封不動塞進抽屜。

## 關鍵決策(brainstorming 定案)

1. **rail 保留**(58px、icon-only),手機僅補滿 ≥44px 觸控目標。理由:7 個目的地 > bottom-nav 上限 5;窄桌面用 bottom-nav 突兀。
2. **手機斷點 `MOBILE = 768`**,寫進 `core/layout-breakpoints.ts`(單一真相源,沿用既有 940/1180 模式)。
   - `≥ 768`:現有桌面行為**完全不變**(可收合側欄、edge 箭頭、手動覆寫)。
   - `< 768`:進「手機抽屜模式」—— 圖牆吃滿剩餘寬,兩側板改抽屜。
   - 選 768 而非沿用 1180:768–1180 之間圖牆夠寬,側欄當欄不會擠爛;只有真的窄(< 768)才需要抽屜。也根治原 bug(原 bug 出現在 ~510px)。
3. **觸發方式:**
   - facet/tree → grid topbar 一顆**「篩選」鈕(僅 < 768 顯示)** 開左抽屜。
   - inspector → **點圖自動滑出右抽屜**看詳情;抽屜內 ⤢ 放大鈕照常開 lightbox。
4. **關閉鈕固定在抽屜 header 條**(標題 + X),永不蓋內容 → ⤢ 不再被疊。

## 元件設計

### `core/ui/drawer-panel.ts`(新增,共用核心)

防打地鼠的關鍵:facet 與 inspector 共用同一支。

- **selector:** `app-drawer-panel`
- **inputs:** `open: boolean`、`side: 'left' | 'right'`、`title: string`
- **outputs:** `(close)`
- **內容:** `<ng-content>` 投影(放現有 facet-sidebar / folder-tree-sidebar / inspector)
- **結構:** 半透明背景遮罩(scrim)+ 滑入面板;面板頂部 header 條(`title` + 關閉 X 鈕)+ 可捲動內容區
- **行為:**
  - `role="dialog"` + `aria-modal="true"` + `[attr.aria-label]="title"`
  - CDK `cdkTrapFocus cdkTrapFocusAutoCapture`(焦點移入;關閉後焦點還原由觸發處負責)
  - Esc 關、點 scrim 關 → emit `(close)`
  - 滑入/淡入動畫(`transform` + `opacity`),`prefers-reduced-motion` 下關閉動畫
  - z-index 高於圖牆與 edge 控制;低於 lightbox(lightbox 1200,抽屜用 ~600)
  - 關閉 X 鈕 ≥ 44px 觸控目標、`aria-label="關閉"`
- **左/右:** `side='left'` 從左緣(rail 右側起)滑入;`side='right'` 從右緣滑入。右抽屜(inspector)寬度可全幅(max ~360px),左抽屜 ~86vw（max ~330px)。

單元測試:open 時渲染、close 時不渲染、Esc/scrim/X 各觸發一次 `(close)`、`side` 對應 class。

### gallery-view / browse-view 改動

新增 computed:`mobile = stageWidth > 0 && stageWidth < MOBILE`。

- **桌面(`!mobile`):** 現狀完全不變(gridCols 三欄、edge 箭頭、facet/inspector 當欄)。
- **手機(`mobile`):**
  - gridCols 收為 `rail 不在此 grid;facet/inspector 不佔欄` → 版面只有 `1fr`(圖牆滿寬);facet/tree 與 inspector 改放進 `<app-drawer-panel>`。
  - edge 箭頭(et-left/et-right)隱藏。
  - **左抽屜:** `<app-drawer-panel side="left" [open]="facetDrawerOpen()" title="篩選" (close)="facetDrawerOpen.set(false)"><app-facet-sidebar/></app-drawer-panel>`(browse 換 folder-tree + 標題「資料夾」)。
  - **右抽屜:** `<app-drawer-panel side="right" [open]="inspectorDrawerOpen()" title="圖片詳情" (close)="inspectorDrawerOpen.set(false)"><app-inspector [photoId]="store.selectedId()" (expand)="openLightbox()"/></app-drawer-panel>`(標題用靜態「圖片詳情」,避免 view 跨 InspectorStore 取檔名的耦合;檔名仍顯示在 inspector 內容上方)。
  - **自動開右抽屜:** `effect` 監看 `store.selectedId()`;手機模式下變為非 null → `inspectorDrawerOpen.set(true)`。關閉抽屜不清 selectedId(再點同圖會重開,見下)。
  - **重開同一張:** 點圖一律 `inspectorDrawerOpen.set(true)`(grid 的選取流程已呼叫 select;另在 view 提供 openInspector() 確保同圖也能重開)。

### grid topbar「篩選」鈕(觸發左抽屜)

- photo-grid / browse-grid 的 topbar 新增**僅手機顯示**的「篩選」鈕,emit `(openFilter)` 給上層 view → `facetDrawerOpen.set(true)`。
- 桌面 `@media (min-width: 768px)` 隱藏該鈕(桌面用 edge 箭頭)。

## 觸控 / a11y

- rail `.act`、「篩選」鈕、抽屜 header X 補滿 ≥ 44px。
- 抽屜沿用 ③h a11y 基礎(role=dialog、focus trap、Esc、scrim 點擊關)。
- 現有 topbar/toolbar ≤640 折疊(「⋯ 更多」溢出)維持不動。
- 焦點還原:抽屜關閉後焦點回到觸發鈕(篩選鈕 / 被點的 tile)—— 由 view 在關閉時處理或交給 CDK。

## 測試

- `drawer-panel.spec.ts`:open/close/Esc/scrim/side。
- e2e 新增 `e2e/mobile-drawers-smoke.mjs`(viewport < 768):
  1. 「篩選」鈕開左抽屜、header X 關。
  2. 點圖自動開右抽屜、詳情顯示、⤢ 鈕**可點不被疊**(座標不重疊 header X)。
  3. 圖牆維持滿寬(無 102px 擠爛)。
  4. 桌面寬度(≥768)不出現抽屜、維持三欄(回歸保護)。
- 既有 a11y / lightbox / browse / rwd e2e 維持綠。

## 受影響檔案(預估)

- 新增:`core/ui/drawer-panel.ts` + `.spec.ts`、`e2e/mobile-drawers-smoke.mjs`、本 spec。
- 改:`core/layout-breakpoints.ts`(+MOBILE)、`gallery-view.ts`、`browse-view.ts`、`photo-grid.html/.ts`(+篩選鈕/openFilter)、`browse-grid.html/.ts`(同)、`package.json`(e2e script)。
- 桌面樣式/邏輯不動。
