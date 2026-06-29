// Lightbox 大圖 e2e(Phase 1 遷移自 lightbox-smoke.mjs)。
// 對齊 docs/design/2026-06-29-e2e-test-hardening.md §二 八條鐵則:
//   web-first 斷言(expect 輪詢)、禁硬等、locator 優先序(getByRole 優先)、
//   禁 ElementHandle、禁 force、URL 走 baseURL、測試隔離、單一行為、mock 即真相。
//
// 覆蓋(來源 .mjs 各段行為):
//   1. inspector「放大」鈕 → 開 lightbox、原圖 <img> 指向 /file。
//   2. 下載連結為 attachment(href 帶 download=true、指向原圖 /file)。
//   3. ←→ 換圖(下一張 / 上一張,計數隨之變動)。
//   4. Esc 關閉 lightbox(等卸載)。
import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { installApiMock } from './fixtures';

// viewport 沿用來源 .mjs:桌面 1440(inspector 為右側欄,放大鈕直接可見)。
const DESKTOP = { width: 1440, height: 900 };

// 對齊來源:root=1、path=Pixiv。
const BROWSE_URL = '/browse?root=1&path=Pixiv';

// 全程 mock /api(鐵則 #5):不依賴真實圖庫 / 不碰原圖。
test.beforeEach(async ({ page }) => {
  await installApiMock(page);
});

// 共用步驟:選一張圖 → 按 inspector「放大」→ 回傳已可見的 lightbox dialog。
// 抽成 helper 讓每個 test 仍可獨立重跑(非共享狀態,只是重複動作)。
async function openLightbox(page: Page) {
  await page.goto(BROWSE_URL);
  // 等首格 tile 出現(資料已載入),不靠 networkidle / 硬等。
  await page.getByTestId('masonry-item').first().click();
  // inspector「放大檢視原圖」鈕(桌面右側欄;aria-label 具名 → getByRole)。
  await page.getByRole('button', { name: '放大檢視原圖' }).click();
  const lightbox = page.getByRole('dialog', { name: /圖片檢視/ });
  await expect(lightbox).toBeVisible();
  return lightbox;
}

test.describe('Lightbox 大圖檢視(桌面)', () => {
  test.use({ viewport: DESKTOP });

  test('放大鈕開 lightbox,原圖 <img> 指向 /file', async ({ page }) => {
    const lightbox = await openLightbox(page);
    // 原圖 <img> 有 alt(檔名)→ 具名 role=img;但 lightbox 裝飾 icon svg 尚無 aria-hidden
    // (P2 a11y gap:全站 svg 加 aria-hidden),同樣被當成 role=img。故用 accessible name
    // (檔名,含副檔名)鎖定原圖那張,避免 strict 命中 4 個無名 svg。src 應指向原圖 /file。
    await expect(lightbox.getByRole('img', { name: /\.\w+$/ })).toHaveAttribute(
      'src',
      /\/api\/photos\/\d+\/file/,
    );
  });

  test('下載連結帶 download=true,指向原圖', async ({ page }) => {
    const lightbox = await openLightbox(page);
    // <a aria-label="下載原圖"> → role=link;href 帶 download=true(attachment 下載)。
    await expect(lightbox.getByRole('link', { name: '下載原圖' })).toHaveAttribute(
      'href',
      /\/api\/photos\/\d+\/file\?download=true/,
    );
  });

  test('下一張 / 上一張換圖(計數隨之變動)', async ({ page }) => {
    const lightbox = await openLightbox(page);

    // 計數元件無 role/testid,退用 CSS;以 toHaveText 輪詢(web-first,非一次性讀取)。
    // TODO: lightbox 計數可補 data-testid 後改 getByTestId。
    const counter = lightbox.locator('.lb-meta .count');
    await expect(counter).toHaveText(/^1 \//);

    // 下一張:計數 1 → 2(具名按鈕 → getByRole)。
    await lightbox.getByRole('button', { name: '下一張(→)' }).click();
    await expect(counter).toHaveText(/^2 \//);

    // 上一張:計數回 2 → 1。
    await lightbox.getByRole('button', { name: '上一張(←)' }).click();
    await expect(counter).toHaveText(/^1 \//);
  });

  test('Esc 關閉 lightbox', async ({ page }) => {
    const lightbox = await openLightbox(page);
    await page.keyboard.press('Escape');
    // open=false 時 @if 移除整個 dialog → 等卸載;toBeHidden 涵蓋 detached。
    await expect(lightbox).toBeHidden();
  });
});
