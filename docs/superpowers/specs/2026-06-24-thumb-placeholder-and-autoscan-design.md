# 縮圖佔位狀態 + 新增來源自動掃描 設計

**日期**:2026-06-24
**分支**:feat/frontend-followups
**範圍**:兩個前端 UX 修正(問題 1 純前端;問題 2 前端為主,store API 回傳值微調)。

## 背景與問題

匯入圖庫後按「重新掃描」,掃描器先建 DB row 再產縮圖檔,中間有空窗:query 查得到 photo,但縮圖端點(`Program.cs` `OpenThumbAsync`,`File.Exists` 為 false)回 404,前端 `<img>` 無 `(error)` 處理 → 掉成瀏覽器預設破圖 icon,且 `loading="lazy"` 載一次定生死,縮圖之後生出來也不會自動補上。

另一個問題:新增圖庫來源(root)後不會自動掃描(`manage.store.ts` `createRoot` 只建來源 + reload 清單),使用者得手動再去按「重新掃描」。

UX 判讀(ui-ux-pro-max):
- **問題 1** 命中 Loading States(High)+ Empty States(Medium):非同步期間用 skeleton、無內容給有意義佔位,不可掉破圖/空白。需區分「掃描中縮圖還沒生出來」(暫時性,應自癒)與「真的沒縮圖」(壞檔/產生失敗,永久)。
- **問題 2** 命中 Confirmation Dialogs 反模式:confirm 對話框是擋**破壞性/不可逆**動作用的。新增來源後掃描是非破壞性、且是該動作天經地義的下一步 —— 套 confirm 反而加無謂摩擦。正解是「直接做 + 明確回饋(toast)」。

決策(已與使用者定案):問題 2 自動排掃描 + toast;問題 1 skeleton + 自動重試 + 失敗佔位。

## 問題 1 設計:`Thumb` 元件(`core/ui/thumb`)

把縮圖的載入/重試/佔位狀態抽成獨立小元件,`photo-grid` 改用它。理由:狀態機集中、可獨立理解與測試;reconcile / inspector 之後共用同一顆。

**介面(Inputs)**
- `photoId: number`(必填)—— 內部用 `PmApi.thumbUrl(photoId)` 組 src。
- `aspectRatio: string`(預設 `'1/1'`)—— 由呼叫端傳真實長寬比(沿用現有 `aspect(p)`),wrapper 固定 `aspect-ratio` 預留空間,避免 CLS。
- `alt: string`(預設 `''`)—— 縮圖為策展用視覺,空 alt 視為裝飾;佔位本身 `aria-hidden`。

**狀態機**
```
loading ──(img load)──> loaded
loading ──(img error)──> retrying ──(load)──> loaded
                              │
                              └─(重試耗盡)─> broken
```
- `loading`:顯示 `.skeleton` 微光(`styles.css` 既有 primitive;reduced-motion 由全域 base 規則凍結為靜態淡色塊)。img 仍掛載但未顯示(或 opacity 0),`(load)` / `(error)` 驅動狀態。
- `retrying`:`(error)` 觸發指數退避重試,重設 `src` 並帶遞增的 cache-bust query(`?r=<attempt>`)強制重載。延遲序列約 `[400, 800, 1600, 3000, 5000]` ms(上限 5 次,總約 ~10s),覆蓋掃描中縮圖空窗;每次重試期間維持 skeleton。
- `broken`:重試耗盡 → 靜態「無縮圖」佔位:灰底(token 色)+ 置中 SVG 圖示(破圖/圖片 icon,Lucide 風格,描邊一致),容器 `aria-hidden="true"`。

**生命週期**
- 重試用的 timer 在元件 `OnDestroy` 清除(無限捲會大量建立/回收 tile,避免 leak)。
- `photoId` 變動(同一 tile 被 track 重用的情境)→ 重置狀態回 `loading`、清 timer。

**樣式落點**(依 CLAUDE.md 前端樣式慣例)
- 元件 `.css`:wrapper aspect-ratio、img object-fit、broken 佔位版面 —— 一律 `var(--token)`,不 `@apply`/`@reference`。
- skeleton 沿用全域 `.skeleton` primitive(寫在 template `class`)。
- 佔位灰底 / 圖示色走既有 token(如 `--color-surface-*` / `--color-t-meta`),不寫裸 hex。

**接線**
- `photo-grid.html`:`<img class="art" ...>` 換成 `<app-thumb [photoId]="p.id" [aspectRatio]="aspect(p)" />`,外層 `.tile` 點選/選取邏輯不動。
- `photo-grid.ts`:`thumb(id)` 可移除(改由 Thumb 內部組 url);`aspect(p)` 保留。
- reconcile / inspector 改用 `<app-thumb>`:**選做收尾**,不阻塞本次。

## 問題 2 設計:新增來源後自動掃描

- `pm-api.ts` `createRoot` 已回傳 `Root`(含 id),無需改 API。
- `manage.store.ts` `createRoot` 由 `Promise<void>` 改為回傳建立的 `Root`(仍 `await loadRoots()` 刷新清單)。
- `roots.ts` `submitAdd`:
  1. `const root = await this.store.createRoot(n, p)`。
  2. `this.toast.success('已新增來源「…」')`、關閉 inline 表單。
  3. 直接呼叫既有 `await this.onRescan(root.id)` —— 重用 scanning 狀態標記、輪詢、完成 toast(「掃描完成:新增 N 張…」)。
- 例外:新增本身失敗 → 既有 error 流程;新增成功但掃描啟動/輪詢失敗 → `onRescan` 的 catch 已 toast「掃描啟動失敗」,來源仍已建立(下次可手動重掃),不 rollback。

## 不做(YAGNI)

- 不加「是否立即掃描」confirm 對話框(非破壞性動作的反模式)。
- 不加表單 opt-out 勾選框(預設自動掃描即可,要停可不掃下次再說;先不增 UI)。
- 不改後端掃描順序(先產縮圖再 expose photo):壞檔 / 產縮圖失敗永遠可能存在,前端 fallback 本就是必要且充分的解;改順序徒增風險。

## 測試與驗證

- 前端自動測試覆蓋有限(CLAUDE.md):若 Thumb 的狀態機(重試次數、loading→loaded→broken 轉換)有現成 spec runner 可掛,以 TDD 寫元件單元測試;否則以手測為主。
- 手測(必做):`ng build` + 起 app →
  1. 新增一個來源 → 確認自動進入掃描、列顯示掃描中、完成 toast。
  2. 掃描進行中看圖牆 → 確認先 skeleton、縮圖陸續「長出來」自動補上、無破圖 icon。
  3. 對一張刻意無縮圖 / 壞檔 → 確認重試耗盡後落到靜態「無縮圖」佔位。
- a11y:破圖佔位 `aria-hidden`;skeleton 期間不誤報內容;reduced-motion 下 skeleton 為靜態。

## 影響檔案

- 新增:`src/Pm.Web/src/app/core/ui/thumb/`(元件 ts/html/css)。
- 改:`photo-grid.html`、`photo-grid.ts`、`manage.store.ts`(`createRoot` 回傳值)、`roots.ts`(`submitAdd`)。
- 選做:`reconcile`、`inspector` 改用 `<app-thumb>`。
