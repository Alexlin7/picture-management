// 手機抽屜 e2e(Phase 1 遷移自 mobile-drawers-smoke.mjs)。
// 對齊 docs/design/2026-06-29-e2e-test-hardening.md §二 八條鐵則:
//   web-first 斷言(expect 輪詢)、禁硬等、locator 優先序(getByRole 優先)、
//   禁 ElementHandle、禁 force、URL 走 baseURL、測試隔離、單一行為、mock 即真相。
//
// 覆蓋(主測 /browse:資料夾樹左抽屜 + inspector 右抽屜):
//   1. 手機圖牆滿寬(無被側欄擠爛)。
//   2. 「資料夾」鈕開左抽屜。
//   3. header X 關左抽屜(等卸載)。
//   4. 點圖自動開右抽屜(inspector)、詳情顯示。
//   5. 右抽屜放大鈕與 header 關閉 X 座標不重疊(根治原疊鈕 bug)。
//   6. 放大鈕可點 → 開 lightbox、Esc 關。
//   7. 桌面寬不出現抽屜、維持三欄(回歸保護)。
import { test, expect } from '@playwright/test';
import { installApiMock } from './fixtures';

// viewport 寬度沿用來源 .mjs:手機 480、桌面 1440(MOBILE 門檻 768)。
const MOBILE = { width: 480, height: 900 };
const DESKTOP = { width: 1440, height: 900 };

// 此頁要載入的資料夾(對齊來源:root=1、path=Pixiv)。
const BROWSE_URL = '/browse?root=1&path=Pixiv';

// 全程 mock /api(鐵則 #8):不依賴真實圖庫 / 不碰原圖。
test.beforeEach(async ({ page }) => {
  await installApiMock(page);
});

test.describe('手機抽屜(viewport < 768)', () => {
  test.use({ viewport: MOBILE });

  test.beforeEach(async ({ page }) => {
    await page.goto(BROWSE_URL);
    // 等實質元素(首格 tile)出現代表進入手機模式且資料已載入,不靠 networkidle / 硬等。
    await expect(page.getByTestId('masonry-item').first()).toBeVisible();
  });

  test('圖牆滿寬,不被側欄擠爛', async ({ page }) => {
    // 用 expect.poll(web-first 輪詢)量寬,不做一次性讀取後手動比較。
    await expect
      .poll(async () => (await page.getByTestId('center-stage').boundingBox())?.width ?? 0)
      .toBeGreaterThanOrEqual(360);
  });

  test('「資料夾」鈕開左抽屜', async ({ page }) => {
    await page.getByRole('button', { name: '開啟資料夾樹' }).click();
    await expect(page.getByRole('dialog', { name: '資料夾' })).toBeVisible();
  });

  test('header X 關左抽屜', async ({ page }) => {
    await page.getByRole('button', { name: '開啟資料夾樹' }).click();
    const leftDrawer = page.getByRole('dialog', { name: '資料夾' });
    await expect(leftDrawer).toBeVisible();

    // header 的關閉 X(aria-label="關閉")限縮在左抽屜內。
    await leftDrawer.getByRole('button', { name: '關閉' }).click();
    // open=false 時 @if 移除整個 scrim → 等卸載(對齊來源 detached 等待法);toBeHidden 涵蓋 detached。
    await expect(leftDrawer).toBeHidden();
  });

  test('點圖自動開右抽屜(inspector),詳情顯示', async ({ page }) => {
    await page.getByTestId('masonry-item').first().click();
    const rightDrawer = page.getByRole('dialog', { name: '圖片詳情' });
    await expect(rightDrawer).toBeVisible();
    // 詳情已渲染:身分區塊(SHA-256)出現。
    await expect(rightDrawer.getByText('SHA-256 身分')).toBeVisible();
  });

  test('放大鈕與 header 關閉 X 不重疊(根治疊鈕 bug)', async ({ page }) => {
    await page.getByTestId('masonry-item').first().click();
    const rightDrawer = page.getByRole('dialog', { name: '圖片詳情' });
    const zoomBtn = rightDrawer.getByRole('button', { name: '放大檢視原圖' });
    const closeBtn = rightDrawer.getByRole('button', { name: '關閉' });
    await expect(zoomBtn).toBeVisible();
    await expect(closeBtn).toBeVisible();

    // 兩鈕矩形不得相交;用 expect.poll 輪詢直到佈局穩定(非一次性讀取)。
    await expect
      .poll(async () => {
        const z = await zoomBtn.boundingBox();
        const x = await closeBtn.boundingBox();
        if (!z || !x) return true; // 尚未量到 → 視為未通過,繼續輪詢
        return !(
          z.x + z.width <= x.x ||
          z.x >= x.x + x.width ||
          z.y + z.height <= x.y ||
          z.y >= x.y + x.height
        );
      })
      .toBe(false);
  });

  test('放大鈕可點,開 lightbox 並可 Esc 關', async ({ page }) => {
    await page.getByTestId('masonry-item').first().click();
    const rightDrawer = page.getByRole('dialog', { name: '圖片詳情' });
    await rightDrawer.getByRole('button', { name: '放大檢視原圖' }).click();

    const lightbox = page.getByRole('dialog', { name: /圖片檢視/ });
    await expect(lightbox).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(lightbox).toBeHidden();
  });
});

test.describe('桌面寬回歸(viewport ≥ 768)', () => {
  test.use({ viewport: DESKTOP });

  test.beforeEach(async ({ page }) => {
    await page.goto(BROWSE_URL);
    await expect(page.getByTestId('masonry-item').first()).toBeVisible();
  });

  test('不出現抽屜、維持三欄', async ({ page }) => {
    // 手機才渲染的「資料夾」鈕在桌面不存在(@if mobile())。
    await expect(page.getByRole('button', { name: '開啟資料夾樹' })).toHaveCount(0);
    // 無任何抽屜遮罩殘留。
    await expect(page.locator('.dp-scrim')).toHaveCount(0);
    // 三欄結構:桌面才渲染的資料夾樹側欄與檢視器同時可見。
    await expect(page.locator('app-folder-tree-sidebar')).toBeVisible();
    await expect(page.locator('app-inspector')).toBeVisible();
  });
});
