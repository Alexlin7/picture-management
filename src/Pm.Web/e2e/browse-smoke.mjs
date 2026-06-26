// /browse 瀏覽器層 e2e:用 page.route 在瀏覽器端 mock /api(空 DB 也能渲染真實 UI),
// 驗證 F1(切夾無交叉污染)+ F6(無限捲自動補頁不停擺),並截圖。
// 跑法:先 `dotnet run` 起 app(serve 已 build 的前端),再 `node e2e/browse-smoke.mjs`。
import { chromium } from 'playwright';
import { mkdirSync } from 'fs';

const BASE = process.env.BASE ?? 'http://localhost:5180';
const OUT = process.env.OUT ?? 'e2e/shots';
mkdirSync(OUT, { recursive: true });

// ---- mock 資料 ----
const TREE = {
  name: '圖庫', relPath: '', photoCount: 320, children: [
    { name: 'Pixiv', relPath: 'Pixiv', photoCount: 210, children: [
      { name: '2023', relPath: 'Pixiv/2023', photoCount: 90, children: null },
      { name: '2024', relPath: 'Pixiv/2024', photoCount: 120, children: [
        { name: '蔚藍檔案', relPath: 'Pixiv/2024/蔚藍檔案', photoCount: 64, children: null },
      ] },
    ] },
    { name: 'Twitter', relPath: 'Twitter', photoCount: 80, children: null },
    { name: '個人照片', relPath: '個人照片', photoCount: 30, children: null },
  ],
};
const ROOTS = [
  { id: 1, name: '圖庫', photoCount: 320 },
  { id: 2, name: 'Twitter 備份', photoCount: 0 },
];
const TAGS = [
  { name: '1girl', kind: 'general', count: 180 }, { name: 'smile', kind: 'general', count: 95 },
  { name: 'blue_archive', kind: 'copyright', count: 64 }, { name: 'arona', kind: 'character', count: 40 },
  { name: 'dress', kind: 'general', count: 33 },
];

// 依 root+path 給不同 id 區段 → 切夾時可斷言不交叉。
function seedOf(rootId, path) {
  let s = rootId * 7;
  for (const c of path) s = (s * 31 + c.charCodeAt(0)) >>> 0;
  return s % 9;
}
function searchPage(rootId, path, afterId) {
  const base = seedOf(rootId, path) * 1000 + 100;   // 各夾不同 id 區段
  const top = afterId == null ? base + 130 : afterId - 1;
  const floor = base;                                // 該夾共 ~130 張
  const ids = [];
  for (let i = top; i > top - 60 && i > floor; i--) ids.push(i);
  const last = ids.length ? ids[ids.length - 1] : floor;
  const nextCursor = last > floor + 1 ? last - 1 : null;
  return {
    items: ids.map((id) => ({ id, fileHash: String(id), width: 300, height: 200 + ((id * 37) % 260) })),
    nextCursor,
  };
}
function svgThumb(id) {
  const hue = (id * 47) % 360;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="300" height="300">
    <rect width="300" height="300" fill="hsl(${hue} 55% 45%)"/>
    <text x="150" y="165" font-size="46" fill="white" text-anchor="middle" font-family="monospace">${id}</text></svg>`;
}

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });

await page.route('**/api/**', async (route) => {
  const req = route.request();
  const url = new URL(req.url());
  const p = url.pathname;
  const json = (body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

  if (p === '/api/folder-roots') return json(ROOTS);
  if (/^\/api\/roots\/\d+\/folder-tree$/.test(p)) return json(TREE);
  if (p === '/api/browse/folder-tags') return json(TAGS);
  if (p === '/api/search/count') {
    const b = req.postDataJSON() ?? {};
    const total = b.all?.length ? 12 : 130;   // 有夾內 tag → 變少(示意 AND)
    return json({ total });
  }
  if (p === '/api/search') {
    const b = req.postDataJSON() ?? {};
    return json(searchPage(b.rootId ?? 1, b.pathPrefix ?? '', b.afterId ?? null));
  }
  if (/^\/api\/photos\/\d+\/thumb$/.test(p)) {
    const id = Number(p.match(/photos\/(\d+)/)[1]);
    return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: svgThumb(id) });
  }
  return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
});

const fail = (msg) => { console.error('ASSERT FAIL:', msg); process.exitCode = 1; };
const tileIds = () => page.$$eval('.tile app-thumb img', (imgs) =>
  imgs.map((im) => Number((im.getAttribute('src') || '').match(/photos\/(\d+)/)?.[1])).filter(Boolean));

try {
  // --- 總覽 ---
  await page.goto(`${BASE}/browse`, { waitUntil: 'networkidle' });
  await page.waitForSelector('.tile', { timeout: 15000 });
  await page.screenshot({ path: `${OUT}/01-browse-overview.png`, fullPage: false });
  console.log('shot: 01-browse-overview.png  tiles=', (await tileIds()).length);

  // --- F6:無限捲自動補頁 ---
  const before = (await tileIds()).length;
  await page.evaluate(() => {
    const v = document.querySelector('.view');
    if (v) v.scrollTop = v.scrollHeight;
  });
  await page.waitForTimeout(1200);
  const after = (await tileIds()).length;
  if (after <= before) fail(`F6 無限捲未補頁:before=${before} after=${after}`);
  else console.log(`F6 OK:捲動後自動補頁 ${before} → ${after}`);
  await page.screenshot({ path: `${OUT}/02-infinite-scroll.png`, fullPage: false });

  // --- F1:切夾無交叉污染 ---
  await page.goto(`${BASE}/browse?root=1&path=Pixiv%2F2024`, { waitUntil: 'networkidle' });
  await page.waitForSelector('.tile');
  await page.waitForTimeout(400);
  const idsA = await tileIds();
  const seedA = (Math.min(...idsA) - 100) / 1000 | 0;

  await page.goto(`${BASE}/browse?root=1&path=Twitter`, { waitUntil: 'networkidle' });
  await page.waitForSelector('.tile');
  await page.waitForTimeout(400);
  const idsB = await tileIds();
  const seedB = (Math.min(...idsB) - 100) / 1000 | 0;

  const overlap = idsA.filter((id) => idsB.includes(id));
  if (seedA === seedB) fail('F1 兩夾 id 區段相同,測試資料無鑑別度');
  else if (overlap.length) fail(`F1 切夾後混入舊夾圖:${overlap.slice(0, 5)}`);
  else console.log(`F1 OK:Pixiv/2024(${idsA.length})與 Twitter(${idsB.length})無交叉`);

  // 夾內疊 tag 帶 + 麵包屑入鏡
  await page.screenshot({ path: `${OUT}/03-folder-switch.png`, fullPage: false });
  console.log('shot: 03-folder-switch.png');

  // 自動完成下拉(夾內再篩)
  await page.click('.addinput');
  await page.fill('.addinput', 'a');
  await page.waitForSelector('.ac-pop', { timeout: 3000 }).catch(() => {});
  await page.screenshot({ path: `${OUT}/04-inner-tag-autocomplete.png`, fullPage: false });
  console.log('shot: 04-inner-tag-autocomplete.png');
} catch (e) {
  fail(e.message);
} finally {
  await browser.close();
}
console.log(process.exitCode ? 'E2E: 有失敗' : 'E2E: 全部通過');
