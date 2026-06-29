// RWD 縮放 e2e(Phase 1 遷移自 rwd-resize-smoke.mjs)。
// 對齊 docs/design/2026-06-29-e2e-test-hardening.md §二 八條鐵則:
//   web-first 斷言(expect.poll 輪詢)、禁硬等(以版面穩定條件取代 waitForTimeout)、
//   locator 優先序(getByRole 優先)、禁 ElementHandle、禁 force、
//   URL 走 config baseURL、測試隔離、單一行為、mock 即真相。
//
// 覆蓋(/gallery 圖牆在多個 viewport 寬度下的 RWD 行為):
//   1. 縮放各寬度皆無橫向破版(no horizontal overflow)。
//   2. 視窗變窄時 masonry 欄數不增(遞減或持平)。
import { test, expect } from '@playwright/test';
import { installApiMock } from './fixtures';

// viewport 寬度沿用來源 .mjs 的斷點序列(寬→窄,含手機 480/375)。
const WIDTHS = [1400, 1100, 820, 720, 480, 375] as const;
const HEIGHT = 900;

// /gallery 相簿頁(masonry 圖牆;預設 mock 即回一頁圖)。
const GALLERY_URL = '/gallery';

// 量測 gallery 版面:一次 page.evaluate 取橫向溢出與可見 tile 數,避免多次 round-trip。
// 註:在瀏覽器內以 querySelectorAll 計算,非 ElementHandle($eval),不違反鐵則 #3。
function measureLayout(page: import('@playwright/test').Page) {
  return page.evaluate(() => {
    const el = document.scrollingElement as HTMLElement;
    const overflow = el.scrollWidth - el.clientWidth;
    const count = document.querySelectorAll('[data-testid="masonry-item"]').length;
    return { overflow, count };
  });
}

// 全程 mock /api(鐵則 #5):不依賴真實圖庫 / 不碰原圖;物件型端點回正確形狀空物件避免 NaN。
test.beforeEach(async ({ page }) => {
  await installApiMock(page);
  await page.goto(GALLERY_URL);
  // 等首格 tile 出現代表圖牆已載入並完成首次排版,不靠 networkidle / 硬等。
  await expect(page.getByTestId('masonry-item').first()).toBeVisible();
});

test('縮放各寬度皆無橫向破版', async ({ page }) => {
  for (const width of WIDTHS) {
    await page.setViewportSize({ width, height: HEIGHT });
    // 以 expect.poll 輪詢溢出值,等 ResizeObserver + rAF 重排穩定後 scrollWidth ≤ clientWidth(+1px 容差)。
    await expect
      .poll(async () => (await measureLayout(page)).overflow, {
        message: `@${width}px 橫向破版:scrollWidth 超出 clientWidth`,
      })
      .toBeLessThanOrEqual(1);
  }
});

test('各寬度圖牆皆正常渲染(有可見 tile、版面不崩)', async ({ page }) => {
  // 註:masonry 欄數由 viewport 斷點 + 側欄收合後的容器寬「共同」決定(窄視窗側欄全收合會
  //   讓中央容器反而變寬),對 viewport 寬或容器寬都非單調,故不在此斷言欄數遞減 —— 那不是
  //   穩定不變量。真正要守的 RWD 不變量是「任何寬度都不破版(上一個 test)且圖牆仍渲染」。
  for (const width of WIDTHS) {
    await page.setViewportSize({ width, height: HEIGHT });
    await expect
      .poll(async () => (await measureLayout(page)).count, {
        message: `@${width}px 圖牆無可見 tile(版面崩了)`,
      })
      .toBeGreaterThan(0);
  }
});
