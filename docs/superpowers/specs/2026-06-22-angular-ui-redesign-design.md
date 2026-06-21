# Angular 前端重構 + UI 美化 設計（2026-06-22）

對齊 `docs/mockups/ui-preview.html` 的視覺與互動,同時把前端寫法現代化。**本輪只做 UI 與互動殼(mock 資料、按鈕先到位),真實 API 綁定為下一輪。**

## 1. 背景與現況

`src/Pm.Web`(**Angular 22 / TypeScript 6 / standalone / 已用 signals**)。問題:

- 元件 template/styles 全內嵌 `.ts`(Vue-SFC 風);emoji 當圖示;手刻 `var(--…)` CSS,離 mockup 精緻度差很遠。
- 無 router —— `@switch (view())` 單一 shell 切換,所有 view 打包在一起、無 code-split。
- 僅 3 欄(活動列 + 主區 + inspector),**缺 mockup 的第 2 欄 facet 側欄**(作品/角色 DAG 樹、屬性、年份)。
- 仍用 `@Output() EventEmitter`、`@Input() set`(舊 member decorator)。

## 2. 決策(已與使用者確認)

| # | 決策 | 選定 |
|---|---|---|
| 1 | UI 庫 | **PrimeNG(unstyled)+ Tailwind v4**。Tailwind 扛版面/樣式,PrimeNG 只挑行為重元件(Table、Dialog、Select、VirtualScroller、Overlay) |
| 2 | PostCSS | 隨 Tailwind v4 的 `@tailwindcss/postcss` 一併導入 |
| 3 | 裝飾器 | 消滅 member decorator → `input()`/`output()`/`model()`/`viewChild()`;`@Component` 保留(decorator-less 未 GA) |
| 4 | 路由 | **Shell + lazy 子路由**:常駐 workbench 殼 + 每個 view 用 Angular Router lazy 載入、獨立 code-split |
| 5 | 範圍 | 全 5 個 view 一次到位(mock 資料) |
| 6 | 基礎建設 | 三項全做:decorator→signal、template/styles 拆出獨立檔、mockup 設計 token 收進 Tailwind theme |

## 3. 架構

```
app.routes.ts ── Shell(活動列常駐)
  └ <router-outlet>
      /gallery     (預設) GalleryView = TopbarSearch + FacetSidebar + PhotoGrid + Inspector
      /import      ImportConfirmView(路徑→tag 確認表)
      /reconcile   ReconcileView(失蹤待辦匣)
      /saved       SavedSearchesView(收藏的搜尋)
      /roots       RootsView(圖庫來源)
```

- **Shell**:左側 58px 活動列,`routerLink` + `routerLinkActive` 高亮;其餘交給 `<router-outlet>`。每個 view 自管內部版面(只有 gallery 是「側欄+grid+inspector」多欄;管理類是單一主區)。
- **Lazy**:每個 view 走 `loadComponent`,各自 code-split。
- **設計 token**:mockup `:root` 的色票/字級/圓角 → Tailwind v4 `@theme`(`--color-canvas`、`--color-panel`、`--color-accent`、tag taxonomy 六色、`--font-display/body/mono` 等)。全站只引用 token,不再散落 hex。
- **共用 primitives**(`ui/`):`btn`(primary/ghost)、`tag-chip`(分色 + 信心度 + 虛線=wd14)、`pill`、`facet-row` 等,以 Tailwind component class 或小元件提供。
- **Mock 資料**:`mock/mock-data.ts` 提供與 `PmApi` 型別相容的假資料(照片、DAG 樹、saved、roots、pending、reconcile),view 先注入 mock,真實 API 下輪替換。

## 4. 元件清單(workflow 平行單位)

地基(我先做,建立所有共享檔與空殼,避免平行衝突):router、`@theme` token、`index.html` 字型、`app.config`(provideRouter/animations/PrimeNG)、shell、各 view 容器骨架、`ui/` primitives、`mock/` 資料。

平行 agent(各佔獨立資料夾,只填自己的 `.ts/.html/.css`):

1. `gallery/facet-sidebar` — 作品/角色 DAG 樹(可展開、多父 ↟2 標記)、屬性、年份
2. `gallery/photo-grid` — masonry tile(hover chips、可能是照片 badge、N 份 dup flag)+ 頂欄 token 搜尋列
3. `inspector/inspector` — 身分→位置簽名、tag lanes(分色)、WD14 建議(✓/✕)、EXIF
4. `manage/import-confirm` — 路徑段→tag 確認表(動作 pill + 分類選擇)
5. `manage/reconcile` — 失蹤待辦卡片(縮圖 + 上次位置 + 三動作)
6. `manage/saved-searches` — 收藏搜尋卡 + 側欄
7. `manage/roots` — 來源清單(狀態點、檔數、重新掃描)

## 5. 寫法規範(所有元件一致)

- standalone + `@Component`;**輸入輸出用 signal**:`input()`/`input.required()`/`output()`/`model()`;`viewChild()` 取代 `@ViewChild`。
- template→`*.html`、styles→`*.css`(或純 Tailwind class,複雜處才寫 `.css`)。
- 顏色/字級/圓角一律走 Tailwind theme token,不再內嵌 hex。
- 互動接上(切換、展開、選取、按鈕 hover/active)但資料來自 mock service;真實 `PmApi` 呼叫留 TODO。

## 6. 驗證

- `ng build` 通過(無 TS error)。
- `ng serve` 後用 **Playwright 開 Chrome**,逐一截圖 `/gallery`(含選圖後 inspector)、`/import`、`/reconcile`、`/saved`、`/roots`,與 mockup 對應畫面比對,補齊明顯落差。

## 7. 非目標(本輪不做)

- 真實 API 綁定(下輪)。
- WD14 / ML、embedding。
- 響應式手機版(維持桌面 workbench;沿用 mockup 的 1180px 斷點即可)。
