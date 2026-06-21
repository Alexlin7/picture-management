# Angular UI 重構 —— 未處理項目 / 下一輪待辦（2026-06-22）

承接 `2026-06-22-angular-ui-redesign-design.md`。本輪交付的是**對齊 mockup 的 UI/互動殼**,
全部用 `src/Pm.Web/src/app/mock/mock-data.ts` 的假資料,「按鈕先到位」。以下是**刻意留到下一輪**的事。

---

## 1. 前端:各元件待接真實 API / 待補互動

目前每個元件都從 `mock/mock-data.ts` 取資料、互動只改本地 signal。下一輪逐一換成 `PmApi`。

| 元件 | 現況(mock + 本地互動) | 待接 / 待補 |
|---|---|---|
| `gallery/photo-grid` | 30 張假圖、漸層 `artGradient` 當縮圖;搜尋框不送查詢;掃描/儲存搜尋按鈕無動作;檢視切換只切 signal | 接 `PmApi.search`(布林 token → all/none)、`thumbUrl(id)` 換掉漸層、keyset 無限捲動(現無分頁)、儲存搜尋接 `POST /api/saved-searches`、掃描接 `POST /api/roots/{id}/scan` |
| `gallery/facet-sidebar` | DAG 樹/屬性/年份全 mock;點 facet 列不會篩選 | 需要**標籤樹/facet 端點**(目前後端無);點列要注入查詢、跟 photo-grid 連動(共用 search 狀態) |
| `inspector/inspector` | 接 input photoId 但讀 `getMockPhoto`;WD14 ✓✕ 只記本地 `decisions` signal | 改接 `GET /api/photos/{id}`;✓ 採用 → 寫 `manual` tag(端點待補);tag 新增/刪除 UI 與端點 |
| `manage/import-confirm` | 路徑段 mock;「分類 ▾」只切本地 cat;套用全部/略過全部 → `console.log` | 接 `GET /api/roots/{id}/pending-segments` + `POST /api/path-rules`(端點已存在);套用後可呼 `apply-path-tags` |
| `manage/reconcile` | 失蹤卡 mock;三動作只切本地 state | 接 `GET /api/reconcile/missing`;**「移到圖庫外 / 已刪除」需要新端點**(軟刪 archive / 硬刪 purge,目前只有 GET) |
| `manage/saved-searches` | 卡片 mock;點卡只記 active signal | 接 `GET/POST/DELETE /api/saved-searches`(端點已存在,client 缺方法);點卡 → 帶查詢跳 `/gallery` |
| `manage/roots` | 來源 mock;重新掃描只標 scanning;新增來源 TODO | 接 `GET /api/roots`、`POST /api/roots/{id}/scan`;新增來源要表單 + `POST /api/roots`(端點已存在,但缺資料夾挑選器) |

---

## 2. `PmApi` client(`src/Pm.Web/src/app/api/pm-api.ts`)待補方法

後端已有、但 client 還沒包的:

- `savedSearches()` / `createSavedSearch(dto)` / `deleteSavedSearch(id)` → `GET/POST/DELETE /api/saved-searches`
- `applyPathTags(rootId)` → `POST /api/roots/{id}/apply-path-tags`

client 也沒有、且**後端也還沒有**的(見 §3):標籤樹 / facet 計數、reconcile 動作、寫 manual tag。

---

## 3. 後端 API 缺口（`src/Pm.Api`,下一輪要新增的端點）

現有端點:`/api/roots`(GET/POST)、`/api/roots/{id}/scan`、`/api/reconcile/missing`(GET)、
`/api/roots/{id}/pending-segments`、`/api/path-rules`、`/api/roots/{id}/apply-path-tags`、
`/api/search`、`/api/photos/{id}`、`/api/photos/{id}/thumb`、`/api/saved-searches`(GET/POST/DELETE)。

**缺**(UI 已有按鈕、後端尚無):

1. **標籤樹 / facet** —— 側欄的「作品/企劃 → 角色」DAG、屬性、年份計數。需要一支回傳 tag 階層 + 命中數的端點(例 `GET /api/tags/tree`、`GET /api/facets?query=…`)。
2. **reconcile 動作** —— 失蹤圖的「移到圖庫外(軟刪 archived)」「已刪除(硬刪 purge)」。需 `POST /api/photos/{id}/archive`、`DELETE /api/photos/{id}`(或 location 層級)。對齊鐵則:預設軟刪,硬刪需明示。
3. **寫 manual tag** —— inspector 採用 WD14 建議 / 手動加標籤 / 移除標籤。需 `POST /api/photos/{id}/tags`、`DELETE /api/photos/{id}/tags/{tagId}`,`source=manual`。

---

## 4. 讓 .NET 單程序也能 serve SPA

目前 `Pm.Api` 只註冊 API 端點,根路徑(5180/)不會吐 Angular —— 開發看 UI 走 `ng serve`(4200)。
要達成 CLAUDE.md 的「單一 exe 雙擊即開」,`Program.cs` 需補:

- `app.UseStaticFiles()`(serve `wwwroot` 的 Angular 靜態檔)
- `app.MapFallbackToFile("index.html")`(SPA 深層路由 fallback,讓 `/gallery` 等重新整理不 404)
- 確認 `ng build` 的 `outputPath` 已是 `../Pm.Api/wwwroot`(現況如此)

---

## 5. UI 庫:PrimeNG 待 Angular 22 版

本輪因 **PrimeNG 最新僅 v21、peer 鎖 Angular/CDK 21**,與本專案 Angular 22 衝突,改用 Tailwind v4 +
內建 Angular CDK。待 PrimeNG 釋出 Angular 22 相容版,再評估把重型元件(資料表、Dialog、Select、
VirtualScroller、Overlay)換成 PrimeNG(unstyled 模式,保留現有 Tailwind 視覺)。屆時程式碼改動應有限。

---

## 6. 其他延後項

- **無限捲動 / keyset 分頁**:photo-grid 現為固定 30 張 mock;真實圖庫十萬量級,需 keyset cursor + CDK virtual scroll(舊版 gallery 有,重構後待重接)。
- **搜尋狀態進 URL**:facet 點選、token 搜尋目前不反映在路由 query,無法分享/重整保留。
- **響應式**:維持桌面 workbench(沿用 mockup 的 1180px 斷點,窄屏隱藏 inspector);手機版未做。
- **a11y**:鍵盤操作、ARIA、焦點環尚未檢視。
- **WD14 / ML 與語意搜尋**:屬 Phase 2,非本輪範圍。

---

## 7. 環境註記(已處理,記錄備查)

- `ng` 指令一度壞掉 —— 是 **npm cache 內 @angular/cli tarball 損壞**(缺 `bin/ng.js`)。已 `npm cache clean --force` + 刪 `node_modules/@angular/cli` 重裝修復,`npm run build/start/test` 正常。日後若再遇 `Cannot find module …/bin/ng.js`,同法處理。
- 縮圖 `GET /api/photos/{id}/thumb` 的 500 已修(相對路徑改 `Path.GetFullPath`,commit `730035a`)。
