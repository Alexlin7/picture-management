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
import { installApiMock, searchPage } from './fixtures';

// viewport 寬度沿用來源 .mjs 的斷點序列(寬→窄,含手機 480/375)。
const WIDTHS = [1400, 1100, 820, 720, 480, 375] as const;
const HEIGHT = 900;

// /gallery 相簿頁(masonry 圖牆;預設 mock 即回一頁圖)。
const GALLERY_URL = '/gallery';

// 量測 gallery 版面:一次 page.evaluate 取橫向溢出與 masonry 欄數,
// 避免多次 round-trip。欄數 = masonry tile(absolute 定位)distinct Math.round(left) 個數。
// 註:在瀏覽器內以 querySelectorAll 計算,非 ElementHandle($eval),不違反鐵則 #3。
function measureLayout(page: import('@playwright/test').Page) {
  return page.evaluate(() => {
    const el = document.scrollingElement as HTMLElement;
    const overflow = el.scrollWidth - el.clientWidth;
    const items = Array.from(
      document.querySelectorAll('[data-testid="masonry-item"]'),
    ) as HTMLElement[];
    const cols = new Set(
      items.map((e) => Math.round(parseFloat(getComputedStyle(e).left))),
    ).size;
    return { overflow, cols, count: items.length };
  });
}

// 全程 mock /api(鐵則 #5):不依賴真實圖庫 / 不碰原圖;物件型端點回正確形狀空物件避免 NaN。
test.beforeEach(async ({ page }) => {
  // 對齊來源 .mjs:tile 寬 800(較寬)。fixtures 預設寬 300 會因窄視窗側欄收合→中央圖牆變寬
  // 而讓欄數「反增」(非本測試要驗的行為);用較寬 tile 還原「欄數隨寬遞減/持平」的語意。
  await installApiMock(page, {
    search: (b) => searchPage(b.rootId ?? 1, b.pathPrefix ?? '', b.afterId ?? null, { width: 800, pageSize: 60 }),
  });
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

test('視窗變窄時 masonry 欄數不增(遞減或持平)', async ({ page }) => {
  let prevCols = Number.POSITIVE_INFINITY;
  for (const width of WIDTHS) {
    await page.setViewportSize({ width, height: HEIGHT });
    // 重排穩定的訊號:橫向溢出已消解(窄化時舊 left 會暫時凸出造成 overflow,重排後歸零)。
    // 未穩定時回 Infinity 迫使 poll 續等;穩定後回欄數,斷言不大於前一(較寬)寬度的欄數。
    await expect
      .poll(
        async () => {
          const { overflow, cols, count } = await measureLayout(page);
          if (count === 0 || overflow > 1) return Number.POSITIVE_INFINITY;
          return cols;
        },
        { message: `@${width}px 欄數未隨寬遞減(前次 ${prevCols} 欄)` },
      )
      .toBeLessThanOrEqual(prevCols);

    // 取穩定後欄數作為下一輪(更窄寬度)的上界。此讀取僅供控制流程傳遞,斷言已由上方 poll 完成。
    prevCols = (await measureLayout(page)).cols;
  }
});
