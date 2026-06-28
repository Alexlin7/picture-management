// Lightbox 大圖 e2e:從 inspector「⤢ 放大」開啟 → 原圖檢視、←→ 換圖、Esc 關。
// page.route mock /api(含 /api/photos/{id}/file 原圖 + /api/photos/{id} detail)。
// 跑法:先 `dotnet run` 起 app,再 `node e2e/lightbox-smoke.mjs`。
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
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
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
const text = (sel) => page.$eval(sel, (e) => e.textContent?.trim()).catch(() => null);

try {
  await page.goto(`${BASE}/browse?root=1&path=Pixiv`, { waitUntil: 'networkidle' });
  await page.waitForSelector('.m-item.roving', { timeout: 15000 });

  // 選一張圖 → inspector 顯示 → 出現「⤢ 放大」鈕
  await page.click('.m-item.roving');
  await page.waitForSelector('.zoom-btn', { timeout: 5000 });
  console.log('OK:選圖後 inspector 出現放大鈕');

  // 開 lightbox
  await page.click('.zoom-btn');
  await page.waitForSelector('.lb[role="dialog"]', { timeout: 5000 });
  if (!(await page.$('.lb-img'))) fail('lightbox 無原圖 <img>');
  else console.log('OK:lightbox 開啟,原圖顯示');
  const imgSrc = await page.$eval('.lb-img', (e) => e.getAttribute('src'));
  if (!imgSrc?.includes('/file')) fail(`原圖 src 應指向 /file(got ${imgSrc})`);
  else console.log(`OK:原圖 src=${imgSrc}`);
  const c1 = await text('.lb-meta .count');
  console.log(`計數:${c1}`);
  await page.screenshot({ path: `${OUT}/lightbox-open.png` });

  // 下載鈕為 attachment 連結
  const dl = await page.$eval('.lb-tools a.iconbtn', (e) => e.getAttribute('href'));
  if (!dl?.includes('download=true')) fail(`下載連結應帶 download=true(got ${dl})`);
  else console.log('OK:下載鈕指向原圖 attachment');

  // ← → 換圖:counter 變化 + img src 變化
  const src1 = await page.$eval('.lb-img', (e) => e.getAttribute('src'));
  await page.keyboard.press('ArrowRight');
  await page.waitForTimeout(150);
  const src2 = await page.$eval('.lb-img', (e) => e.getAttribute('src'));
  const c2 = await text('.lb-meta .count');
  if (src2 === src1) fail('ArrowRight 未換圖(src 不變)');
  else console.log(`OK:→ 換圖 ${src1} → ${src2}(計數 ${c1} → ${c2})`);

  // Esc 關閉
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);
  if (await page.$('.lb[role="dialog"]')) fail('Esc 未關閉 lightbox');
  else console.log('OK:Esc 關閉 lightbox');
} catch (e) {
  fail(e.message);
} finally {
  await browser.close();
}
console.log(process.exitCode ? 'LIGHTBOX E2E: 有失敗' : 'LIGHTBOX E2E: 全部通過');
