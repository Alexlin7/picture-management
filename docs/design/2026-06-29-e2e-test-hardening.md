---
status: active
last-reviewed: 2026-06-29
supersedes: []
superseded-by: []
related: [2026-06-26-frontend-rwd-design, 2026-06-28-mobile-drawers-design, 2026-06-24-frontend-design-guidelines]
---

# E2E 測試強化 + 準則 — 設計

- 日期:2026-06-29
- 狀態:**準則已定案(新寫 e2e 一律遵守)**;既有 5 支煙霧腳本**已完成遷移並刪除**(5 支 `.mjs` → 7 支 `.spec.ts`,見 §四)。
- 範圍:① 盤點現有 `src/Pm.Web/e2e/*.mjs` 的壞味道;② 確立「之後寫 e2e 一律遵守」的鐵則;③ 遷移到 `@playwright/test` runner 的分階段計畫。
- 來源:2026-06-29 對 5 支 e2e 腳本的 code review。
- 鐵則對照:守主專案鐵則 #1(e2e 全程 `page.route` mock `/api`,**不依賴真實圖庫、不碰原圖**)、#8(localhost 單機,無認證 → 無 `storageState` 登入態問題)。

---

## 一、現況(審查結論)

現有 e2e **不是** `@playwright/test` runner + `playwright.config`,而是用 Playwright **library API 手寫的獨立 Node 煙霧腳本**(`node e2e/*.mjs`,經 `package.json` script 觸發)。因此沒有 config(無 `baseURL`/`storageState`/`retries`/`testIdAttribute`)、沒有 `test()`/`expect()`。

5 支腳本與覆蓋:

| 腳本 | 覆蓋 |
|---|---|
| `browse-smoke.mjs` | `/browse` 切夾無交叉污染、無限捲補頁、夾內 tag autocomplete |
| `rwd-resize-smoke.mjs` | `/gallery` 多 viewport 無橫向破版 + 欄數隨寬遞減 |
| `a11y-keyboard-smoke.mjs` | skip-link/nav、roving tabindex、role=button/listbox/option、方向鍵 + 自動補頁 |
| `lightbox-smoke.mjs` | inspector 放大 → lightbox 開/換圖/下載連結/Esc |
| `mobile-drawers-smoke.mjs` | 手機抽屜開關、疊鈕不重疊、桌面寬回歸 |

**核心病灶:整套缺 auto-wait 與 web-first 重試。** 大量硬等(`waitForTimeout`)直接餵給斷言,加上 ElementHandle(`page.$`/`$eval`/`$$eval`)一次性讀取,兩者疊加是 flaky 的主因。

### 1.1 問題清單(依嚴重度)

**🔴 高(會 flaky / 誤判)**

- **硬等驅動斷言**:`browse-smoke.mjs:107`(`waitForTimeout(1200)` 後比 tile 數,補頁慢於 1.2s 即假性失敗,最危險)、`rwd-resize-smoke.mjs:60`、`a11y-keyboard-smoke.mjs:115/120/130`、`lightbox-smoke.mjs:82/90`。
- **一次性讀取當斷言、無重試**:`page.$eval(sel,…).catch(()=>null)` 讀一次,元素晚到回 null → 誤判。遍布 `a11y:54/61/65/71/89/102/110/121/126`、`lightbox:51/65/67/75/80/83/91`、`mobile:64/88/89/108`、`browse:91`。
- **方向鍵補頁迴圈時序脆**:`a11y-keyboard-smoke.mjs:128-133`(每步 45ms × 200,`stall<10` 可能早於補頁觸發 → 假性「未補頁」)。

**🟡 中(維護性)**

- **ElementHandle 舊 API、不 auto-wait**:`page.$`/`$$`/`$eval`/`$$eval`(位置同上)。註:同檔 `page.click`/`fill`/`waitForSelector` 有 auto-wait、可接受,問題只在 `$` 系列。
- **Locator 全用 CSS class**:`.tile`/`.m-item`/`.ac-pop`/`.lb`/`.dp-panel`/`.zoom-btn`。諷刺的是測試本身一直斷言 `role`,正好該用 `getByRole`(`lightbox:64/91` `.lb[role=dialog]`、`a11y:96/98` `.ac-pop`/`.ac-row`、`mobile:71/83` `.dp-panel[role=dialog]`)。
- **單腳本斷言過載、無隔離**:每支線性流程共用單一 `page`,`waitForSelector` 逾時 throw → 被外層 `try` 接住 → **後續檢查全跳過**。`a11y-keyboard-smoke.mjs` 把 7 個獨立主題綁一條流程,最嚴重。
- **缺 web-first 斷言**:全套無 `expect`,以無重試手動斷言代之(治本見 §三)。

**🟢 低(風格)**

- `rwd-resize-smoke.mjs:6` env 變數名 `PM_E2E_BASE ?? BASE`,與其他四支(只用 `BASE`)不一致。
- `BASE` 預設 `http://localhost:5180` 雖可由 env 覆寫,但散落各檔(應收斂到 config `baseURL`)。
- `browse-smoke.mjs:138`、`a11y:80` `waitForSelector(...).catch(()=>{})` 把「元素沒出現」這個真問題靜音。
- `browse-smoke.mjs:96` 依賴 `waitUntil:'networkidle'`,但本 app 有 `tagging/stats` 輪詢,networkidle 時機不穩 → 建議改等實質元素。

### 1.2 已經做對的(列為標準範式)

- **`mobile-drawers-smoke.mjs:77/100/105`** 用 `waitForSelector(..., { state: 'detached' })` 等卸載、不靠固定延遲 —— **全套最佳範本,新測試照抄此等待法**。
- **無任何 `force: true`**(清單第 6 項乾淨)。
- **登入重複 N/A**:單機無認證,不需 `storageState`。
- **PrimeNG N/A**:用 Angular CDK,非 PrimeNG。
- **全程 `page.route` mock `/api`**:空 DB 也能渲染真實 UI,守鐵則 #1(不碰真實圖庫)。

---

## 二、準則(鐵則 —— 新寫 / 改寫 e2e 一律遵守)

> 以下為**不可違反**的 e2e 規則;既有違反處列入 §四遷移逐步收斂,新程式碼**不得**新增違反。

1. **一律 web-first 斷言**。用 `await expect(locator).toHaveText/toHaveAttribute/toBeVisible/toHaveCount(...)`(內建輪詢重試)。
   - **禁止** `const x = await loc.textContent(); if (x !== …) fail()` 這類一次性讀取後手動比較,**禁止** `expect(await loc.textContent())`(把 await 包進 expect)。
2. **禁止硬等**。不得用 `waitForTimeout` / `sleep` 當等待手段;一律等具體條件:`expect` 輪詢、`page.waitForFunction(...)`、`locator.waitFor({ state })`、`waitForSelector({ state: 'detached' })`。
   - 唯一例外:視覺截圖前需等動畫「定格」且無可觀測 DOM 訊號時,可短等,但**必須**在該行註明理由。
3. **Locator 優先序**:`getByRole` > `getByLabel` / `getByText` > `getByTestId` > CSS。已有 `role`/`aria` 的元件(dialog/listbox/option/combobox/button)**一律走 `getByRole`**。純視覺容器無語意時補 `data-testid`(對齊 config `testIdAttribute`)。
   - **禁止** ElementHandle:`page.$` / `page.$$` / `page.$eval` / `page.$$eval`(不 auto-wait)。改用 `page.locator(...)` + web-first 斷言或 `locator.evaluate()`。
4. **禁止 `force: true`**(除非該行註明「為何不是在掩蓋真問題」)。元素不可互動是真 bug,不要繞過。
5. **URL 走 config `baseURL`**,測試內**不得**散落 `http://localhost:...`;由 `playwright.config` 統一(含 `webServer` 自動起 app)。
6. **測試隔離**:每個 `test()` 可單獨重跑、不依賴前一個 test 的殘留狀態;`page.route` mock 在 `beforeEach`(或 fixture)重建。**禁止** `describe.serial` 跨 test 共享可變狀態。
7. **單一 test 聚焦單一行為**;不把多個不相關斷言塞進一條流程(過載會讓首個逾時連累後段)。一個主題一個 `test()`。
8. **mock 即真相**:全程 `page.route('**/api/**', …)`,守鐵則 #1(不依賴真實圖庫 / 不碰原圖);物件型端點回「正確形狀的空物件」而非 `[]`(避免 `undefined.map` / `NaN`,見現有腳本註解)。

---

## 三、目標架構:`@playwright/test`

採 `@playwright/test` runner(`@playwright/test` 已在 `devDependencies`),新增 `src/Pm.Web/playwright.config.ts`:

```ts
import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  retries: process.env.CI ? 2 : 0,
  use: {
    baseURL: process.env.PM_E2E_BASE ?? 'http://localhost:5180',
    testIdAttribute: 'data-testid',
    trace: 'on-first-retry',
  },
  // 取代「先手動 dotnet run」:runner 自動起 app、serve 已 build 的前端
  webServer: {
    command: 'dotnet run --project ../../src/Pm.Api',
    url: 'http://localhost:5180',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
```

**一步同時解掉 §一的 #1/#2/#3/#4/#6/#7**:`expect` 自帶輪詢、locator 自帶 auto-wait、`test()` 可隔離重跑、`webServer` 取代手動起 app、`baseURL` 收斂 URL。`page.route` mock 邏輯幾乎可逐段沿用(搬進 `beforeEach`)。

> 為何不只是「把 .mjs 寫好」:手寫腳本要自行重造輪詢、隔離、報表;runner 是 Playwright 對這些問題的標準解,維護成本最低。

---

## 四、遷移計畫(分階段,小切片)

每片 `npx playwright test <檔>` 綠燈後再 commit;一次只動一支,降低風險。

- **Phase 0(地基)**:加 `playwright.config.ts`(§三)+ `package.json` script 改 `"e2e": "playwright test"`(保留舊 `node e2e/*.mjs` 直到各支遷完)。建立共用 `e2e/fixtures.ts`(集中 `page.route` mock 資料 TREE/ROOTS/TAGS/searchPage/svgThumb,各 spec 引用)。
- **Phase 1**:`mobile-drawers` 先遷(已是最佳範式,轉換成本最低,當樣板)。
- **Phase 2**:`lightbox` → `browse` → `rwd`,逐支轉 `test()`,套 §二鐵則(web-first expect、getByRole、移除 waitForTimeout)。
- **Phase 3**:`a11y-keyboard` 最後(最大、最該拆),拆成多個聚焦 `test()`(shell 結構 / roving / 資料夾 / 麵包屑 / combobox / 方向鍵補頁)。
- **收尾**:刪除舊 `.mjs` 與對應 `package.json` script;README「測試」段更新跑法。

> **遷移結果(2026-07 複查):全數完成。** `e2e/` 下已無 `.mjs`,共 7 支 `.spec.ts`(`a11y-keyboard` / `browse` / `lightbox` / `mobile-drawers` / `rwd-resize` / `saved-a11y` + `fixtures.ts`);`playwright.config.ts`(`baseURL` / `webServer` / `testIdAttribute='data-testid'`)齊備,`package.json` 為 `"e2e": "playwright test"`;上列鐵則零違反(`page.$` / `force:true` / `expect(await` / 硬編 localhost 全零命中)。

**最該優先修的前 3 項**(若先做最小修補、暫不整套遷移):
1. `browse-smoke.mjs:107` 無限捲硬等 → `waitForFunction` 等 tile 數增加。
2. 全套「`waitForTimeout` 緊接斷言」改等具體條件(含 `a11y:128-133` 補頁迴圈)。
3. 落地 §三 runner + config(治本)。

---

## 五、決策日誌

- **採 `@playwright/test` 而非續寫手寫 .mjs**:runner 內建 auto-wait/重試/隔離/報表,一次解多類壞味道;`@playwright/test` 已在相依內,無新增成本。
- **`webServer` 自動起 app**:消除「先手動 `dotnet run`」這個易漏步驟,CI 亦可一鍵跑。
- **沿用 `page.route` mock、不接真 DB**:守鐵則 #1(不碰原圖 / 不依賴真實圖庫),且空 DB 也能測真實 UI;測試快、可重現。
- **保留漸進遷移、不一次重寫**:小切片逐支轉、每片綠燈再 commit,對齊開發約定。
