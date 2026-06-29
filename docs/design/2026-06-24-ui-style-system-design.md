---
status: active
last-reviewed: 2026-06-29
supersedes: []
superseded-by: []
related: [2026-06-24-frontend-design-guidelines]
---

# UI 樣式系統地基 + UI/UX 進化 — 設計文件

- 日期:2026-06-24
- 狀態:**地基(Spec 1)已實作(2026-06-24)** —— `@theme` token + a11y/motion + primitive 三態;後續 UI/UX 進化為持續方向。
- 關聯:`CLAUDE.md`(繁中協作、小切片);`docs/mockups/ui-preview.html`(視覺 token 原始來源);
  既有 `src/Pm.Web/src/styles.css`(`@theme` token + `@layer components` primitive)
- 起因:專案初期以 frontend-design 生成,UI 到一定程度後要進化操作體驗與視覺風格;
  並釐清「掛了 Tailwind 卻一直在寫元件 CSS」的架構準則。設計依據:ui-ux-pro-max
  design-system(STYLE = **Dark Mode (OLED)**:高對比、minimal glow、visible focus、
  hover 150–300ms、prefers-reduced-motion、cursor-pointer)。

## 已鎖定的方向決策(brainstorming 過程)

1. **樣式準則 = 明確化現行 hybrid + 適度 Tailwind 化**(非全面 utility-first,也不開 `@reference`)。
2. **視覺進化 = 精煉現有 + 系統化**(不換識別):保留暗色工作台 + cyan accent
   `#22d3ee` + booru 分色 + 現有字體(Space Grotesk / Inter / JetBrains Mono)。
   ui-ux-pro-max 建議的綠 accent 與 Cormorant 學術字體**不採用**(後者是查詢字含
   "library" 帶歪;密集工作台不適合)。

## 背景:目前樣式架構(讀碼確認)

- **Tailwind v4 已裝妥**:`styles.css` `@import "tailwindcss"` + `.postcssrc.json`
  的 `@tailwindcss/postcss`。無 `tailwind.config`(v4 走 CSS 設定)。
- **Token 唯一真相**:`styles.css` 的 `@theme`(每個 token 同時產 utility `bg-canvas`
  與 CSS 變數 `var(--color-canvas)`),來源為 `docs/mockups/ui-preview.html`。
- **共用 primitive**:`@layer components` 的 `.btn` / `.frow` / `.facet-t` / `.dot`(用 `@apply`)。
- **元件 .css**:10 檔、約 1865 行,手寫 plain CSS + `var(--token)`。
- **根因(本案要解的痛點)**:Angular 元件樣式**隔離編譯**,元件 .css 內**不能**用
  `@apply` / `@tailwind`(`import-confirm.css` 有「嚴禁」註記)。故元件樣式只能手寫,
  造成「掛 Tailwind 卻一直寫 CSS」的觀感。架構本身合理,但缺明文準則與系統軸。

## 範圍拆解(三個 spec,依序)

| Spec | 標題 | 範圍 | 本文 |
|---|---|---|---|
| **1** | **樣式系統地基** | 準則文件化 + 擴充 `@theme` token + 升級共用 primitive | ← **本文** |
| 2 | 各頁 UX 打磨 | focus/hover/active、loading skeleton、空狀態、轉場、a11y,逐頁套地基,webapp-testing 驗證 | 後續另案 |
| 3 | 按鈕補齊 | 免後端者:gallery 儲存搜尋鈕、掃描入口、批次 requeue 入口(見下方盤點) | 後續另案 |

> Spec 2/3 依賴 Spec 1 的地基。本文只交付 **Spec 1**;2/3 僅列範圍備忘,不在此實作。

### 按鈕/接線盤點(給 Spec 3,本文不做)

- gallery 頂列缺「**儲存搜尋**」「**掃描**」入口(端點已有)。
- **批次 requeue** 維護入口缺(`/api/tag/requeue` 後端就緒;目前僅 inspector 有 per-photo retag)。
- 行為層設定頁(自動標排程 on/off、worker 暫停)缺 —— 需後端 `app_setting`(§D),延後。
- 計數類(搜尋總命中、WD14 pending/error)需後端 count 端點,延後。
- roots 新增來源無資料夾挑選器(瀏覽器限制,延後評估)。

## Spec 1 設計

### (a) 樣式準則(文件化,落點:本 spec + 實作時補進 `CLAUDE.md` 前端慣例段)

新樣式的落點決策樹:
1. **能用 Tailwind utility 表達**(間距/排版/顏色/簡單狀態)→ 寫在 **template 的 `class`**。
2. **會跨元件重複的元件樣式** → 進 `@layer components` 的共用 primitive(可 `@apply`)。
3. **元件專屬且複雜**(動畫、複雜 selector、RWD 條件如 inspector tag chip)→ 元件 .css,
   **一律用 `var(--token)`**,不放能用 utility 表達的瑣碎樣式。
4. **顏色/字體/圓角/陰影一律走 token**,不寫裸 hex(既有裸值逐步收斂,不在本 spec 全清)。

規則:元件 .css 不得 `@apply`/`@tailwind`/`@reference`(維持隔離編譯,與現況一致)。

### (b) 擴充 `@theme` token(`styles.css`)

新增以下系統軸(沿用現有命名風格;同時自動產 utility 與 `var`):

```css
@theme {
  /* ... 既有 token 不動 ... */

  /* 語意狀態色(現只有 danger;補 success/warning,與 booru 分色語意分離) */
  --color-success: #34d399;   /* 成功/完成(emerald,刻意異於 character 綠 #4ade80) */
  --color-warning: #f5b454;   /* 警告(amber,異於 meta tag 黃 #fbbf24) */
  /* danger 沿用既有 --color-danger: #f0616d */

  /* focus ring(a11y CRITICAL,現幾乎無) */
  --color-ring: #22d3ee;      /* = accent;以 outline 呈現 */

  /* elevation/shadow 階(暗色宜淡;面板→浮層→彈窗分層) */
  --shadow-1: 0 1px 2px rgba(0,0,0,.40);
  --shadow-2: 0 6px 18px rgba(0,0,0,.50);
  --shadow-3: 0 16px 40px rgba(0,0,0,.60);

  /* motion(統一時長/緩動;搭配 Tailwind duration-*) */
  --ease-out: cubic-bezier(.22, 1, .36, 1);
  --dur-fast: 150ms;
  --dur-base: 200ms;
}
```

> **⚠️ 鐵則(2026-06-29 補):token 區塊必須是 `@theme static { … }`,不可用裸 `@theme {`。**
> Tailwind v4 預設**只輸出「有被 utility class 用到」的 @theme 變數**(tree-shaking)。本專案多數 token 是經 **runtime 組字串的 `var()`**(如 `tag-color.ts` 的 `tagColor(kind)` → `var(--color-t-${kind})`)或**元件 .css 的 `var()`** 引用 —— Tailwind 掃不到字面引用,就會把它們從 `:root` 砍掉,inline `var()` 取不到色。
> 實例:TAG_COLOR 收斂後,`--color-t-copyright` / `-path` / `-manual`(無對應 utility)被 tree-shake,facet「作品/企劃→角色」分色點變無色;改 `@theme static` 後全數輸出修復。
> 驗證:build 後 `grep --color-t- src/Pm.Api/wwwroot/*.css` 應看到所有定義的 token。新增「只給 `var()` 用」的 token 一律靠 static 保證輸出。

### (c) 全域 a11y / motion 基礎(`styles.css` base 段)

```css
/* 鍵盤可見 focus(只在鍵盤操作時出現,不干擾滑鼠) */
:focus-visible {
  outline: 2px solid var(--color-ring);
  outline-offset: 2px;
  border-radius: 3px;
}
/* 尊重系統「減少動態」偏好(ui-ux-pro-max checklist) */
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: .001ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: .001ms !important;
  }
}
```

### (d) 升級 / 新增共用 primitive(`@layer components`)

- **`.btn` 補三態**:`:focus-visible` ring(由全域 `:focus-visible` 覆蓋,確認不被蓋掉)、
  `:active`(輕微下沉/變暗)、`[disabled]`(opacity .45 + `cursor: default` + 不可點)。
- **`.btn-danger`**:危險動作(用 `--color-danger`),與 primary 視覺區隔。
- **`.input`**:文字輸入統一(背景 raised、border hair、focus ring、placeholder 色)。
- **`.skeleton`**:loading 微光(shimmer);**reduced-motion 下退化為靜態淡色塊**。
- **`.elev-1/2/3`** 或直接用 `shadow-*` utility:套 elevation 階到 card/popover/modal。
- 既有 `.btn`/`.frow`/`.dot` 行為不破壞;只增不改既有外觀(除三態補強)。

### 不做(YAGNI / 延後)

- 不全面重寫 1865 行元件 CSS(準則上路後**逐步**收斂,屬 Spec 2 逐頁進行)。
- 不換 accent / palette / 字體(已決策:精煉現有)。
- 不開 `@reference`、不走全面 utility-first。
- 不動後端、不動 `displayOf`/`groupTags` 等邏輯。
- RWD/手機版(目前桌面 workbench 為主;Spec 2 視需要再談)。

## 驗收

- `ng build` 0 錯、`ng test` 既有測試仍綠(本 spec 不改邏輯,純樣式/token)。
- 取一個代表頁(roots 或 gallery 頂列)套新 primitive + focus ring,**Playwright 截圖對照**:
  鍵盤 Tab 有可見 focus ring;hover/active/disabled 三態正確;版面不破。
- `prefers-reduced-motion` 開啟時動畫/轉場停用(可由 DevTools 模擬驗證)。

## 對齊鐵則 / 慣例

- 純前端、不碰 SQLite / 原圖 / 後端邏輯。
- 小切片、逐步 commit;每片 `ng build` + 起 app 手測(本專案前端慣例)。
- token 為唯一真相;改視覺決策同步更新本 spec 與 `docs/mockups/ui-preview.html` 註記。
