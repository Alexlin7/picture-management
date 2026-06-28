// ③g 手機抽屜 e2e(viewport < 768):
//  1. topbar「資料夾」鈕開左抽屜、header X 關。
//  2. 點圖自動開右抽屜(inspector)、詳情顯示。
//  3. 右抽屜內 ⤢ 放大鈕可點、且座標不與 header 關閉 X 重疊(根治原疊鈕 bug)。
//  4. 圖牆滿寬(無 102px 擠爛)。
//  5. 桌面寬(≥768)不出現抽屜、維持三欄(回歸保護)。
// 覆蓋範圍:主測 /browse(資料夾樹+inspector 抽屜)。/gallery 用同一支 DrawerPanel 與對稱 wiring,
//   結構相同,由 ng build + DrawerPanel 單元測試 + 手測覆蓋(facet 樹端點與 browse 不同,不在此 mock)。
// 跑法:先 `dotnet run` 起 app,再 `node e2e/mobile-drawers-smoke.mjs`。
import { chromium } from 'playwright';
import { mkdirSync } from 'fs';

const BASE = process.env.BASE ?? 'http://localhost:5180';
const OUT = process.env.OUT ?? 'e2e/shots';
mkdirSync(OUT, { recursive: true });

const TREE = { name: '圖庫', relPath: '', photoCount: 320, children: [
  { name: 'Pixiv', relPath: 'Pixiv', photoCount: 210, children: [{ name: '2024', relPath: 'Pixiv/2024', photoCount: 120, children: null }] },
  { name: 'Twitter', relPath: 'Twitter', photoCount: 80, children: null }] };
const ROOTS = [{ id: 1, name: '圖庫', photoCount: 320 }];

function searchPage(afterId) {
  const top = afterId == null ? 230 : afterId - 1;
  const ids = [];
  for (let i = top; i > top - 40 && i > 100; i--) ids.push(i);
  const last = ids.length ? ids[ids.length - 1] : 100;
  return { items: ids.map((id) => ({ id, fileHash: String(id), width: 1200, height: 800 })), nextCursor: last > 101 ? last - 1 : null };
}
function svgImg(id, label) {
  const hue = (id * 47) % 360;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="800"><rect width="1200" height="800" fill="hsl(${hue} 55% 40%)"/><text x="600" y="430" font-size="120" fill="white" text-anchor="middle" font-family="monospace">${label} ${id}</text></svg>`;
}
function detail(id) {
  return { id, fileHash: String(id).padStart(8, '0'), width: 1200, height: 800, mime: 'image/svg+xml',
    takenAt: null, cameraModel: null,
    locations: [{ libraryRootId: 1, relPath: `Pixiv/pic_${id}.png`, status: 'present' }], tags: [] };
}

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 480, height: 900 } });
await page.route('**/api/**', async (route) => {
  const p = new URL(route.request().url()).pathname;
  const json = (b) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(b) });
  if (p === '/api/folder-roots') return json(ROOTS);
  if (/^\/api\/roots\/\d+\/folder-tree$/.test(p)) return json(TREE);
  if (p === '/api/browse/folder-tags') return json([]);
  if (p === '/api/search/count') return json({ total: 130 });
  if (p === '/api/search') return json(searchPage(route.request().postDataJSON()?.afterId ?? null));
  if (/^\/api\/photos\/\d+\/thumb$/.test(p)) return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: svgImg(Number(p.match(/photos\/(\d+)/)[1]), '縮圖') });
  if (/^\/api\/photos\/\d+\/file$/.test(p)) return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: svgImg(Number(p.match(/photos\/(\d+)/)[1]), '原圖') });
  if (/^\/api\/photos\/\d+$/.test(p)) return json(detail(Number(p.match(/photos\/(\d+)/)[1])));
  if (p === '/api/tagging/stats') return json({ pending: 0, error: 0, running: 0 });
  return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
});

const fail = (m) => { console.error('ASSERT FAIL:', m); process.exitCode = 1; };

try {
  // ---- 手機寬(480):抽屜模式 ----
  await page.goto(`${BASE}/browse?root=1&path=Pixiv`, { waitUntil: 'networkidle' });
  await page.waitForSelector('.m-item.roving', { timeout: 15000 });

  // 圖牆滿寬:中央 grid 欄寬應接近視窗寬(無被 350px 側欄擠成 ~102px)。
  const stageW = await page.$eval('.center-stage', (e) => e.getBoundingClientRect().width);
  if (stageW < 360) fail(`圖牆被擠爛(center-stage 寬 ${stageW},期望 ≥ 360)`);
  else console.log(`OK:手機圖牆滿寬 center-stage=${Math.round(stageW)}`);

  // 「資料夾」鈕存在 → 開左抽屜。
  await page.waitForSelector('.filter-btn', { timeout: 5000 });
  await page.click('.filter-btn');
  await page.waitForSelector('.dp-panel.left[role="dialog"]', { timeout: 5000 });
  console.log('OK:資料夾鈕開左抽屜');
  await page.screenshot({ path: `${OUT}/mobile-left-drawer.png` });

  // header X 關左抽屜(等 DOM 真的卸載,不靠固定延遲)。
  await page.click('.dp-panel.left .dp-close');
  await page.waitForSelector('.dp-panel.left', { state: 'detached', timeout: 5000 })
    .then(() => console.log('OK:header X 關左抽屜'))
    .catch(() => fail('header X 未關左抽屜'));

  // 點圖 → 自動開右抽屜(inspector)。
  await page.click('.m-item.roving');
  await page.waitForSelector('.dp-panel.right[role="dialog"]', { timeout: 5000 });
  console.log('OK:點圖自動開右抽屜');

  // 右抽屜內 ⤢ 放大鈕存在,且與 header 關閉 X 座標不重疊(根治疊鈕 bug)。
  await page.waitForSelector('.dp-panel.right .zoom-btn', { timeout: 5000 });
  const zoom = await page.$eval('.dp-panel.right .zoom-btn', (e) => e.getBoundingClientRect());
  const x = await page.$eval('.dp-panel.right .dp-close', (e) => e.getBoundingClientRect());
  const overlap = !(zoom.right < x.left || zoom.left > x.right || zoom.bottom < x.top || zoom.top > x.bottom);
  if (overlap) fail(`⤢ 放大鈕與 header X 重疊(zoom ${JSON.stringify(zoom)} / x ${JSON.stringify(x)})`);
  else console.log('OK:⤢ 放大鈕與 header X 不重疊');
  await page.screenshot({ path: `${OUT}/mobile-right-drawer.png` });

  // ⤢ 可點 → 開 lightbox。
  await page.click('.dp-panel.right .zoom-btn');
  await page.waitForSelector('.lb[role="dialog"]', { timeout: 5000 });
  console.log('OK:⤢ 可點,開 lightbox');
  await page.keyboard.press('Escape');
  await page.waitForSelector('.lb[role="dialog"]', { state: 'detached', timeout: 5000 });

  // ---- 桌面寬(1440):無抽屜、維持三欄(回歸保護)----
  // 切寬後等抽屜與篩選鈕都從 DOM 卸載(mobile() 變 false → @if 移除),不靠固定延遲。
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.waitForSelector('.filter-btn', { state: 'detached', timeout: 5000 })
    .then(() => console.log('OK:桌面寬「資料夾」鈕隱藏'))
    .catch(() => fail('桌面寬仍顯示「資料夾」鈕'));
  if (await page.$('.dp-scrim')) fail('桌面寬仍殘留抽屜');
  else console.log('OK:桌面寬無抽屜');
} catch (e) {
  fail(e.message);
} finally {
  await browser.close();
}
console.log(process.exitCode ? 'MOBILE-DRAWERS E2E: 有失敗' : 'MOBILE-DRAWERS E2E: 全部通過');
