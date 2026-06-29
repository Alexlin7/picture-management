// e2e 共用 fixtures:集中 page.route 的 mock 資料與安裝函式。
// 萃取自既有 5 支煙霧腳本(browse / rwd-resize / a11y-keyboard / lightbox / mobile-drawers)。
// 各 *.spec.ts 在 beforeEach 呼叫 installApiMock(page, overrides?) 即可。
//
// 對齊 docs/design/2026-06-29-e2e-test-hardening.md:
//   鐵則 #5 mock 即真相 → 全程攔截 '**/api/**',守主專案鐵則 #1(不碰真實圖庫 / 不碰原圖)。
//   物件型端點一律回「正確形狀的空物件」而非 [](避免 undefined.map / NaN)。
import type { Page, Route } from '@playwright/test';

// ── 型別(僅描述測試需要的形狀,不求與後端 DTO 完全等同)──────────────
export interface FolderTreeNode {
  name: string;
  relPath: string;
  photoCount: number;
  children: FolderTreeNode[] | null;
}
export interface FolderRoot {
  id: number;
  name: string;
  photoCount: number;
}
export interface FolderTag {
  name: string;
  kind: 'general' | 'character' | 'copyright' | 'meta' | string;
  count: number;
}
export interface PhotoListItem {
  id: number;
  fileHash: string;
  width: number;
  height: number;
}
export interface PhotoPage {
  items: PhotoListItem[];
  nextCursor: number | null;
}
export interface PhotoLocation {
  libraryRootId: number;
  relPath: string;
  status: 'present' | 'archived' | string;
}
export interface PhotoDetail {
  id: number;
  fileHash: string;
  width: number;
  height: number;
  mime: string;
  takenAt: string | null;
  cameraModel: string | null;
  locations: PhotoLocation[];
  tags: unknown[];
}
export interface TaggingStats {
  pending: number;
  error: number;
  running: number;
}
export interface TagsTree {
  tree: unknown[];
  rootless: unknown[];
  general: unknown[];
  meta: unknown[];
}
// /api/search 的 POST body(各欄皆選填)。
export interface SearchBody {
  rootId?: number;
  pathPrefix?: string;
  afterId?: number | null;
  all?: unknown[];
}

// ── mock 資料常數 ──────────────────────────────────────────────
// 來源:browse-smoke.mjs(最完整的 TREE);a11y/lightbox/mobile 為其子集。
export const TREE: FolderTreeNode = {
  name: '圖庫',
  relPath: '',
  photoCount: 320,
  children: [
    {
      name: 'Pixiv',
      relPath: 'Pixiv',
      photoCount: 210,
      children: [
        { name: '2023', relPath: 'Pixiv/2023', photoCount: 90, children: null },
        {
          name: '2024',
          relPath: 'Pixiv/2024',
          photoCount: 120,
          children: [
            { name: '蔚藍檔案', relPath: 'Pixiv/2024/蔚藍檔案', photoCount: 64, children: null },
          ],
        },
      ],
    },
    { name: 'Twitter', relPath: 'Twitter', photoCount: 80, children: null },
    { name: '個人照片', relPath: '個人照片', photoCount: 30, children: null },
  ],
};

export const ROOTS: FolderRoot[] = [
  { id: 1, name: '圖庫', photoCount: 320 },
  { id: 2, name: 'Twitter 備份', photoCount: 0 },
];

export const TAGS: FolderTag[] = [
  { name: '1girl', kind: 'general', count: 180 },
  { name: 'smile', kind: 'general', count: 95 },
  { name: 'blue_archive', kind: 'copyright', count: 64 },
  { name: 'arona', kind: 'character', count: 40 },
  { name: 'dress', kind: 'general', count: 33 },
];

// GalleryStore 建構子會輪詢:回正確形狀避免 NaN / undefined.map。
export const TAGGING_STATS: TaggingStats = { pending: 0, error: 0, running: 0 };
export const TAGS_TREE: TagsTree = { tree: [], rootless: [], general: [], meta: [] };

// ── search 分頁回應產生器 ───────────────────────────────────────
// 沿用 browse-smoke.mjs:依 root+path 給不同 id 區段(切夾可斷言不交叉污染)。
export function seedOf(rootId: number, path: string): number {
  let s = rootId * 7;
  for (const c of path) s = (s * 31 + c.charCodeAt(0)) >>> 0;
  return s % 9;
}

export interface SearchPageOptions {
  pageSize?: number; // 一頁筆數(browse/a11y=60、lightbox/mobile=40)
  width?: number; // item 寬(browse/a11y=300、lightbox/mobile=1200)
  height?: number | ((id: number) => number); // item 高(預設依 id 變化造瀑布流)
}

// 依 rootId+path+afterId 產一頁;各夾共 ~130 張。回 PhotoPage。
export function searchPage(
  rootId = 1,
  path = '',
  afterId: number | null = null,
  opts: SearchPageOptions = {},
): PhotoPage {
  const pageSize = opts.pageSize ?? 60;
  const width = opts.width ?? 300;
  const heightOf =
    typeof opts.height === 'function'
      ? opts.height
      : opts.height != null
        ? () => opts.height as number
        : (id: number) => 200 + ((id * 37) % 260);

  const base = seedOf(rootId, path) * 1000 + 100; // 各夾不同 id 區段
  const top = afterId == null ? base + 130 : afterId - 1;
  const floor = base; // 該夾共 ~130 張
  const ids: number[] = [];
  for (let i = top; i > top - pageSize && i > floor; i--) ids.push(i);
  const last = ids.length ? ids[ids.length - 1] : floor;
  const nextCursor = last > floor + 1 ? last - 1 : null;
  return {
    items: ids.map((id) => ({ id, fileHash: String(id), width, height: heightOf(id) })),
    nextCursor,
  };
}

// ── 縮圖 / 原圖 SVG(避免依賴真實圖檔)─────────────────────────────
export function svgThumb(id: number, label = ''): string {
  const hue = (id * 47) % 360;
  const text = label ? `${label} ${id}` : `${id}`;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="300" height="300"><rect width="300" height="300" fill="hsl(${hue} 55% 45%)"/><text x="150" y="165" font-size="46" fill="white" text-anchor="middle" font-family="monospace">${text}</text></svg>`;
}

// 大圖版(1200×800),lightbox / mobile 的 /thumb 與 /file 用。
export function svgImg(id: number, label = ''): string {
  const hue = (id * 47) % 360;
  const text = label ? `${label} ${id}` : `${id}`;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="800"><rect width="1200" height="800" fill="hsl(${hue} 55% 40%)"/><text x="600" y="430" font-size="120" fill="white" text-anchor="middle" font-family="monospace">${text}</text></svg>`;
}

// 單張 photo detail(lightbox / mobile inspector)。
export function detail(id: number): PhotoDetail {
  return {
    id,
    fileHash: String(id).padStart(8, '0'),
    width: 1200,
    height: 800,
    mime: 'image/svg+xml',
    takenAt: null,
    cameraModel: null,
    locations: [{ libraryRootId: 1, relPath: `Pixiv/pic_${id}.png`, status: 'present' }],
    tags: [],
  };
}

// ── route-mock 安裝函式 ─────────────────────────────────────────
// 各端點皆可由 overrides 覆寫;未覆寫者用上方的預設資料/產生器。
export interface InstallApiMockOptions {
  roots?: FolderRoot[];
  tree?: FolderTreeNode;
  folderTags?: FolderTag[];
  tagsTree?: TagsTree;
  taggingStats?: TaggingStats;
  // POST /api/search/count → { total };預設依 body.all 長度示意 AND 變少。
  count?: (body: SearchBody) => { total: number };
  // POST /api/search → PhotoPage。
  search?: (body: SearchBody) => PhotoPage;
  // GET /api/photos/{id} → PhotoDetail。
  detail?: (id: number) => PhotoDetail;
  // GET /api/photos/{id}/thumb → SVG 字串(回 null 則以 204 回應,如 rwd 腳本)。
  thumb?: (id: number) => string | null;
  // GET /api/photos/{id}/file → SVG 字串(原圖)。
  file?: (id: number) => string;
}

// 安裝 '**/api/**' 攔截。在 beforeEach 呼叫即可。
export async function installApiMock(page: Page, overrides: InstallApiMockOptions = {}): Promise<void> {
  const roots = overrides.roots ?? ROOTS;
  const tree = overrides.tree ?? TREE;
  const folderTags = overrides.folderTags ?? TAGS;
  const tagsTree = overrides.tagsTree ?? TAGS_TREE;
  const taggingStats = overrides.taggingStats ?? TAGGING_STATS;
  const countFn =
    overrides.count ?? ((body: SearchBody) => ({ total: body.all?.length ? 12 : 130 }));
  const searchFn =
    overrides.search ??
    ((body: SearchBody) => searchPage(body.rootId ?? 1, body.pathPrefix ?? '', body.afterId ?? null));
  const detailFn = overrides.detail ?? detail;
  const thumbFn = overrides.thumb ?? ((id: number) => svgThumb(id));
  const fileFn = overrides.file ?? ((id: number) => svgImg(id, '原圖'));

  await page.route('**/api/**', async (route: Route) => {
    const req = route.request();
    const p = new URL(req.url()).pathname;
    const json = (body: unknown) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    const svg = (body: string) =>
      route.fulfill({ status: 200, contentType: 'image/svg+xml', body });
    const body = (): SearchBody => {
      try {
        return (req.postDataJSON() as SearchBody) ?? {};
      } catch {
        return {};
      }
    };
    const idOf = (re: RegExp) => Number(p.match(re)![1]);

    if (p === '/api/folder-roots') return json(roots);
    if (/^\/api\/roots\/\d+\/folder-tree$/.test(p)) return json(tree);
    if (p === '/api/browse/folder-tags') return json(folderTags);
    if (p === '/api/tags/tree') return json(tagsTree);
    if (p === '/api/tagging/stats') return json(taggingStats);
    // 先比對 /count(較具體),避免被一般 /search 吞掉。
    if (p === '/api/search/count') return json(countFn(body()));
    if (p === '/api/search') return json(searchFn(body()));
    if (/^\/api\/photos\/\d+\/thumb$/.test(p)) {
      const svgStr = thumbFn(idOf(/photos\/(\d+)/));
      return svgStr == null ? route.fulfill({ status: 204 }) : svg(svgStr);
    }
    if (/^\/api\/photos\/\d+\/file$/.test(p)) return svg(fileFn(idOf(/photos\/(\d+)/)));
    if (/^\/api\/photos\/\d+$/.test(p)) return json(detailFn(idOf(/photos\/(\d+)/)));

    // 其餘陣列型端點(saved-searches…)回空陣列。
    return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
  });
}
