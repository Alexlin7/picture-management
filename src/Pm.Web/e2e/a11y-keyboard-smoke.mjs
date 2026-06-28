// a11y 鍵盤可達 e2e(Wave A):驗證 pmActivate directive 在真實瀏覽器把可點 div/span
// 變成鍵盤可達的 role=button —— tile / 資料夾列 / 麵包屑皆可 Tab 聚焦,Enter 觸發動作。
// 用 page.route mock /api(空 DB 也能渲染),跑法:先 `dotnet run` 起 app,再 `node e2e/a11y-keyboard-smoke.mjs`。
import { chromium } from 'playwright';

const BASE = process.env.BASE ?? 'http://localhost:5180';

const TREE = {
  name: '圖庫', relPath: '', photoCount: 320, children: [
    { name: 'Pixiv', relPath: 'Pixiv', photoCount: 210, children: [
      { name: '2024', relPath: 'Pixiv/2024', photoCount: 120, children: null },
    ] },
    { name: 'Twitter', relPath: 'Twitter', photoCount: 80, children: null },
  ],
};
const ROOTS = [{ id: 1, name: '圖庫', photoCount: 320 }, { id: 2, name: 'Twitter 備份', photoCount: 0 }];
const TAGS = [
  { name: '1girl', kind: 'general', count: 180 }, { name: 'arona', kind: 'character', count: 40 },
];

function searchPage(afterId) {
  const top = afterId == null ? 230 : afterId - 1;
  const ids = [];
  for (let i = top; i > top - 60 && i > 100; i--) ids.push(i);
  const last = ids.length ? ids[ids.length - 1] : 100;
  return {
    items: ids.map((id) => ({ id, fileHash: String(id), width: 300, height: 200 + ((id * 37) % 260) })),
    nextCursor: last > 101 ? last - 1 : null,
  };
}
function svgThumb(id) {
  const hue = (id * 47) % 360;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="300" height="300"><rect width="300" height="300" fill="hsl(${hue} 55% 45%)"/><text x="150" y="165" font-size="46" fill="white" text-anchor="middle" font-family="monospace">${id}</text></svg>`;
}

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });

await page.route('**/api/**', async (route) => {
  const req = route.request();
  const p = new URL(req.url()).pathname;
  const json = (body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  if (p === '/api/folder-roots') return json(ROOTS);
  if (/^\/api\/roots\/\d+\/folder-tree$/.test(p)) return json(TREE);
  if (p === '/api/browse/folder-tags') return json(TAGS);
  if (p === '/api/search/count') return json({ total: 130 });
  if (p === '/api/search') return json(searchPage(req.postDataJSON()?.afterId ?? null));
  if (/^\/api\/photos\/\d+\/thumb$/.test(p)) return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: svgThumb(Number(p.match(/photos\/(\d+)/)[1])) });
  if (p === '/api/tagging/stats') return json({ pending: 0, error: 0, running: 0 });
  return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
});

const fail = (msg) => { console.error('ASSERT FAIL:', msg); process.exitCode = 1; };
const attr = (sel, name) => page.$eval(sel, (el, n) => el.getAttribute(n), name).catch(() => null);

try {
  await page.goto(`${BASE}/browse?root=1&path=Pixiv`, { waitUntil: 'networkidle' });
  await page.waitForSelector('.tile', { timeout: 15000 });

  // 1) tile 透過 pmActivate 取得 role=button + tabindex=0
  if ((await attr('.tile', 'role')) !== 'button') fail('tile 缺 role=button(pmActivate 未生效)');
  else console.log('OK:tile role=button');
  if ((await attr('.tile', 'tabindex')) !== '0') fail('tile 缺 tabindex=0');
  else console.log('OK:tile tabindex=0');

  // 2) 資料夾列(root-row / indent)鍵盤可達
  if ((await attr('.frow.root-row', 'role')) !== 'button') fail('root-row 缺 role=button');
  else console.log('OK:資料夾 root-row role=button');

  // 3) 麵包屑可點段(非當前)鍵盤可達
  const crumbRole = await attr('.crumbs .c:not(.cur)', 'role');
  if (crumbRole !== 'button') fail(`麵包屑可點段缺 role=button(got ${crumbRole})`);
  else console.log('OK:麵包屑可點段 role=button');

  // 4) token × 已是原生 button(夾內再篩):先加一個夾內 tag
  await page.click('.addinput');
  await page.fill('.addinput', 'a');
  await page.waitForSelector('.ac-pop .ac-row', { timeout: 3000 }).catch(() => {});
  const hasRow = await page.$('.ac-pop .ac-row');
  if (hasRow) {
    await page.click('.ac-pop .ac-row');
    await page.waitForSelector('.tchip .x', { timeout: 3000 }).catch(() => {});
    const xTag = await page.$eval('.tchip .x', (el) => el.tagName).catch(() => null);
    if (xTag !== 'BUTTON') fail(`夾內 token × 應為原生 button(got ${xTag})`);
    else console.log('OK:夾內 token × 是原生 button');
  } else {
    console.log('SKIP:無 autocomplete row,略過 token × 檢查');
  }

  // 5) 鍵盤聚焦第一張 tile 後按 Enter → 該 tile 取得 .sel(選取)
  await page.$eval('.tile', (el) => el.focus());
  const focused = await page.evaluate(() => document.activeElement?.classList.contains('tile'));
  if (!focused) fail('tile 無法用 focus() 聚焦(tabindex 失效)');
  else console.log('OK:tile 可程式聚焦');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(200);
  const selExists = await page.$('.tile.sel');
  if (!selExists) fail('Enter 未選取 tile(.sel 未出現)');
  else console.log('OK:聚焦 tile 按 Enter 觸發選取');
} catch (e) {
  fail(e.message);
} finally {
  await browser.close();
}
console.log(process.exitCode ? 'A11Y E2E: 有失敗' : 'A11Y E2E: 全部通過');
