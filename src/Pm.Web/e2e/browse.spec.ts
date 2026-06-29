// /browse 資料夾瀏覽 e2e(Phase 1 遷移自 browse-smoke.mjs)。
// 對齊 docs/design/2026-06-29-e2e-test-hardening.md §二 八條鐵則:
//   web-first 斷言(expect 輪詢)、禁硬等、locator 優先序(getByRole 優先)、
//   禁 ElementHandle、禁 force、URL 走 baseURL、測試隔離、單一行為、mock 即真相。
//
// 覆蓋(來源 browse-smoke.mjs 的 F1 / F6 + 夾內 tag autocomplete):
//   1. /browse 載入後渲染圖牆首頁。
//   2. F6 無限捲到底自動補後續頁(來源 :107 用 waitForTimeout(1200) 量 tile 數 →
//      此處改成 expect(...).toPass() 反覆捲動,等「只可能在後續頁才存在的圖格」可見)。
//   3. F1 切資料夾不交叉污染(各夾首格 id 落在各自 seed 區段)。
//   4. 夾內 tag 自動完成:輸入即列出符合的 tag。
//   5. 選取自動完成項目 → 加入夾內篩選 chip。
import { test, expect } from '@playwright/test';
import { installApiMock, seedOf } from './fixtures';

// viewport 沿用來源 .mjs 桌面寬 1440×900(MOBILE 門檻 768 以上,不進抽屜模式)。
const DESKTOP = { width: 1440, height: 900 };

// fixtures.searchPage:base = seedOf(rootId, path)*1000+100;首頁首格 = base+130(id 由大到小)、
// 各夾共 ~130 張(分 60/60/8 三頁,最末頁最小 id = base+1)。
const baseOf = (path: string, rootId = 1) => seedOf(rootId, path) * 1000 + 100;

test.use({ viewport: DESKTOP });

// 全程 mock /api(鐵則 #5 mock 即真相):不依賴真實圖庫 / 不碰原圖。
test.beforeEach(async ({ page }) => {
  await installApiMock(page);
});

test('/browse 載入後渲染圖牆首頁', async ({ page }) => {
  await page.goto('/browse?root=1&path=Pixiv');
  // 等首格(data-i=0 = 該夾最大 id)出現代表資料已載入,不靠 networkidle / 硬等。
  await expect(page.getByTestId('masonry-item').first()).toBeVisible();
  // 首格 img 指向該夾 seed 區段最大 id(base+130)。
  const firstId = baseOf('Pixiv') + 130;
  await expect(page.locator('[data-testid="masonry-item"][data-i="0"] app-thumb img')).toHaveAttribute(
    'src',
    new RegExp(`/photos/${firstId}/thumb`),
  );
});

test('無限捲到底自動補後續頁(出現後續頁才有的圖格)', async ({ page }) => {
  await page.goto('/browse?root=1&path=Pixiv');
  await expect(page.getByTestId('masonry-item').first()).toBeVisible();

  // 可捲動容器:browse-grid 的 .view(無 role / testid,屬 locator 優先序最末的 CSS 後備)。
  // TODO(元件): .view 可補 data-testid(如 "browse-scroll")讓測試走 getByTestId 而非 CSS。
  const view = page.getByTestId('center-stage').locator('.view');

  // base+1 是該夾「最末頁」最後一格(首頁 60 張只到 base+71),只有持續補頁才會載入並渲染到底。
  // → 以它的可見性當「補頁已生效」的具體條件(取代來源 waitForTimeout(1200) 後比 tile 數)。
  const deepId = baseOf('Pixiv') + 1;
  const deepTile = page.locator(`[data-testid="masonry-item"] app-thumb img[src*="/photos/${deepId}/thumb"]`);

  // 反覆捲到目前內容底部觸發 sentinel IntersectionObserver → 自動補下一頁;
  // 每輪短輪詢 deepTile 是否到底可見,未見則(下一輪)再捲。全程無 waitForTimeout。
  await expect(async () => {
    await view.evaluate((el) => el.scrollTo({ top: el.scrollHeight }));
    await expect(deepTile).toBeVisible({ timeout: 1000 });
  }).toPass({ timeout: 20_000 });
});

test('切資料夾不交叉污染:各夾首格落在各自 seed 區段', async ({ page }) => {
  // 夾 A:Pixiv/2024(seed 7,base 7100)。
  await page.goto('/browse?root=1&path=Pixiv%2F2024');
  const firstA = page.locator('[data-testid="masonry-item"][data-i="0"] app-thumb img');
  await expect(firstA).toHaveAttribute('src', new RegExp(`/photos/${baseOf('Pixiv/2024') + 130}/thumb`));

  // 切到夾 B:Twitter(seed 3,base 3100)—— 首格落在不同區段,未混入 A 的圖。
  await page.goto('/browse?root=1&path=Twitter');
  const firstB = page.locator('[data-testid="masonry-item"][data-i="0"] app-thumb img');
  await expect(firstB).toHaveAttribute('src', new RegExp(`/photos/${baseOf('Twitter') + 130}/thumb`));

  // 鑑別度防呆:兩夾 seed 區段必須不同,否則測資無鑑別力(對齊來源 F1 的 seedA!==seedB 檢查)。
  // 註:此處用 page.goto 全載切夾(對齊來源);store 內 gen-guard 的在途競態由
  //     src/app/features/browse/browse.store.spec.ts 單元測試覆蓋。
  expect(baseOf('Pixiv/2024')).not.toBe(baseOf('Twitter'));
});

test('夾內 tag 自動完成:輸入即列出符合的 tag', async ({ page }) => {
  await page.goto('/browse?root=1&path=Pixiv');
  await expect(page.getByTestId('masonry-item').first()).toBeVisible();

  const combo = page.getByRole('combobox', { name: '夾內再篩標籤' });
  await combo.click();
  await combo.fill('a');

  // 浮層 role=listbox / 項目 role=option(走 getByRole,鐵則 #3 優先序)。
  const listbox = page.getByRole('listbox');
  await expect(listbox).toBeVisible();
  // 預設 TAGS 含 'a' 的:blue_archive、arona(1girl/smile/dress 不含)。
  await expect(listbox.getByRole('option')).toHaveCount(2);
  await expect(listbox.getByRole('option', { name: /arona/ })).toBeVisible();
});

test('選取自動完成項目 → 加入夾內篩選 chip', async ({ page }) => {
  await page.goto('/browse?root=1&path=Pixiv');
  await expect(page.getByTestId('masonry-item').first()).toBeVisible();

  const combo = page.getByRole('combobox', { name: '夾內再篩標籤' });
  await combo.click();
  await combo.fill('arona');
  await page.getByRole('option', { name: /arona/ }).click();

  // chip 出現(移除鈕 aria-label 帶 tag 名),代表已加入夾內篩選並回查。
  await expect(page.getByRole('button', { name: '移除夾內篩選:arona' })).toBeVisible();
});
