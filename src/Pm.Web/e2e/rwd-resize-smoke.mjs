// rwd-resize-smoke.mjs — 縮 viewport 斷言:無橫向破版 + 圖牆 tile 可見 + 欄數隨寬遞減。
// 跑法:先 `dotnet run` 起 app(serve 已 build 的前端),再 `node e2e/rwd-resize-smoke.mjs`。
// 範式仿 browse-smoke.mjs:chromium.launch + page.route('**/api/**', …) mock,導 /gallery。
import { chromium } from 'playwright';

const BASE = process.env.PM_E2E_BASE ?? process.env.BASE ?? 'http://localhost:5180';
const fail = (m) => { console.error('FAIL:', m); process.exitCode = 1; };

// ---- mock 資料(對齊 browse-smoke.mjs 的 /api/search 回傳形狀) ----
// gallery.store 呼叫:
//   /api/search        POST → PhotoPage { items: PhotoListItem[], nextCursor?: number|null }
//   /api/search/count  POST → { total: number }
// PhotoListItem 含 id/fileHash/width/height;thumb 端點 /api/photos/{id}/thumb。
const PHOTOS = Array.from({ length: 60 }, (_, i) => ({
  id: i + 1,
  fileHash: String(i + 1).padStart(64, '0'),
  width: 800,
  height: 1000 + (i % 5) * 100,   // 各異 aspect,確保真實瀑布流
}));

async function mockApi(page) {
  await page.route('**/api/**', (route) => {
    const url = new URL(route.request().url());
    const p = url.pathname;
    const json = (body) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

    // 先比對 /count(較長),避免 includes 把它歸入一般 search。
    if (p === '/api/search/count') return json({ total: PHOTOS.length });
    if (p === '/api/search') return json({ items: PHOTOS, nextCursor: null });
    if (/^\/api\/photos\/\d+\/thumb$/.test(p)) return route.fulfill({ status: 204 });
    // 其餘端點(facet、tags、saved-searches…)回空陣列,讓前端不報錯。
    return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
  });
}

const WIDTHS = [1400, 1100, 820, 720];

(async () => {
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage({ viewport: { width: WIDTHS[0], height: 900 } });
    await mockApi(page);

    // 導到 gallery 頁(路由:/gallery)。
    await page.goto(`${BASE}/gallery`, { waitUntil: 'networkidle' });
    // 等第一批 .m-item 出現(app-masonry 元件的 tile wrapper)。
    await page.waitForSelector('.m-item', { timeout: 15000 });

    let prevCols = Infinity;
    for (const w of WIDTHS) {
      await page.setViewportSize({ width: w, height: 900 });
      // 等 ResizeObserver + rAF debounce 後重排(useStageWidth 用 rAF)。
      await page.waitForTimeout(400);

      // (a) 無橫向破版:scrollWidth <= clientWidth + 1px 容差。
      const overflow = await page.evaluate(() =>
        document.documentElement.scrollWidth - document.documentElement.clientWidth);
      if (overflow > 1) {
        fail(`@${w}px 橫向破版:scrollWidth 超出 clientWidth ${overflow}px`);
      }

      // (b) .m-item wrapper 數 > 0(masonry tile 可見)。
      const tiles = await page.$$eval('.m-item', (els) => els.length);
      if (tiles === 0) {
        fail(`@${w}px 圖牆無可見 tile(.m-item 數為 0)`);
      }

      // (c) 欄數(distinct Math.round(left))不隨寬遞增。
      // .m-item 是 absolute 定位,left 由 computeMasonryLayout 算出並以 inline style 設定。
      const cols = await page.$$eval('.m-item', (els) =>
        new Set(els.map((e) => Math.round(parseFloat(getComputedStyle(e).left)))).size);
      if (cols > prevCols) {
        fail(`@${w}px 欄數未隨寬遞減:前次 ${prevCols} 欄 → 此次 ${cols} 欄`);
      }

      console.log(`ok @${w}px: tiles=${tiles} cols=${cols} overflow=${overflow}px`);
      prevCols = cols;
    }
  } finally {
    await browser.close();
  }

  console.log(process.exitCode ? 'rwd-resize-smoke: 有失敗' : 'PASS rwd-resize-smoke');
})().catch((e) => { console.error(e); process.exitCode = 1; });
