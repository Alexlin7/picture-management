// 本輪 UI 用的假資料(按鈕先到位,真實 API 下輪)。
// 模型沿用 docs/mockups/ui-preview.html 的 PHOTOS / TREE / IMPORT / RECON …,
// 並盡量對齊 src/app/api/pm-api.ts 的型別,日後可平滑換成真實 API。
import { type TagKind } from '@core/tag-color';

export interface MockTag {
  name: string;
  kind: TagKind;
  source: 'wd14' | 'manual' | 'path';
  confidence?: number | null;
}
export interface MockSugg {
  name: string;
  kind: TagKind;
  confidence: number;
}
export interface MockLoc {
  root: string;
  path: string;
}
export interface MockPhoto {
  id: number;
  seed: number;
  ar: number; // aspect ratio (w/h)
  title: string;
  series: string;
  personal: boolean;
  dup: boolean;
  tags: MockTag[];
  sugg: MockSugg[];
  locs: MockLoc[];
}

const FN = [
  '如月れん', '花京院ちえり', 'エリー・コニファー', '不知名', '勇気ちひろ',
  'アルス・アルマル', '椎名唯華', '戌亥とこ', '本間ひまわり', 'レヴィ・エリファ', 'social_pic',
];
const SER = ['2434', 'VSpo!', 'ホロライブ', '我不知道', '原神', 'ブルーアーカイブ', '個人照片'];
const AR = [1.4, 1.5, 0.75, 1.0, 1.33, 0.8];

export const MOCK_PHOTOS: MockPhoto[] = Array.from({ length: 30 }, (_, i) => {
  const personal = i === 7 || i === 22;
  const dup = i % 9 === 4;
  return {
    id: i,
    seed: i * 7 + 3,
    ar: AR[i % 6],
    title: personal ? '2024-08 沖縄旅行' : FN[i % FN.length],
    series: personal ? '個人照片' : SER[i % 6],
    personal,
    dup,
    tags: personal
      ? [
          { name: 'photo', kind: 'general', source: 'wd14', confidence: 0.71 },
          { name: 'realistic', kind: 'general', source: 'wd14', confidence: 0.66 },
          { name: '2024', kind: 'meta', source: 'path' },
        ]
      : [
          { name: FN[i % FN.length], kind: 'character', source: i % 3 ? 'wd14' : 'manual', confidence: i % 3 ? 0.88 : null },
          { name: SER[i % 6] === '我不知道' ? '2434' : SER[i % 6], kind: 'copyright', source: 'wd14', confidence: 0.93 },
          { name: '1girl', kind: 'general', source: 'wd14', confidence: 0.99 },
          { name: ['smile', 'long_hair', 'blue_eyes', 'twintails', 'school_uniform'][i % 5], kind: 'general', source: 'wd14', confidence: 0.82 },
          { name: '2023', kind: 'meta', source: 'path' },
        ],
    sugg: personal
      ? [{ name: 'beach', kind: 'general', confidence: 0.52 }]
      : [
          { name: ['blush', 'ribbon', 'jacket', 'hairband'][i % 4], kind: 'general', confidence: 0.41 },
          { name: FN[(i + 1) % FN.length], kind: 'character', confidence: 0.38 },
        ],
    locs: dup
      ? [
          { root: '本機', path: 'pics\\2023\\2434\\' + FN[i % FN.length] + '_01.png' },
          { root: '舊 GDrive', path: 'G\\圖\\にじ\\' + FN[i % FN.length] + '.png' },
        ]
      : [{ root: i % 3 ? '本機' : '新硬碟', path: (i % 3 ? 'pics' : 'E\\art') + '\\2023\\' + SER[i % 6] + '\\img_' + (1000 + i) + '.' + (i % 4 ? 'jpg' : 'png') }],
  };
});

export const getMockPhoto = (id: number): MockPhoto | undefined => MOCK_PHOTOS.find((p) => p.id === id);

// 預設選一張「2 份」的圖,展示身分→多位置
export const DEFAULT_SELECTED_ID = MOCK_PHOTOS.find((p) => p.dup)?.id ?? 0;

/* ---------- 側欄 facet:作品/角色 DAG 樹 ---------- */
export interface TreeNode {
  name: string;
  kind: TagKind;
  n: number;
  multi?: boolean; // 多父(DAG)
  children?: TreeNode[];
}
export const MOCK_TREE: TreeNode[] = [
  {
    name: 'にじさんじ', kind: 'copyright', n: 1612,
    children: [
      {
        name: '2434', kind: 'copyright', n: 1204,
        children: [
          { name: '如月れん', kind: 'character', n: 182 },
          { name: '勇気ちひろ', kind: 'character', n: 147 },
          { name: '椎名唯華', kind: 'character', n: 96 },
        ],
      },
      {
        name: 'VOLTACTION', kind: 'copyright', n: 188,
        children: [{ name: '戌亥とこ', kind: 'character', n: 74, multi: true }],
      },
    ],
  },
  {
    name: 'VSpo!', kind: 'copyright', n: 642,
    children: [
      { name: '花京院ちえり', kind: 'character', n: 120 },
      { name: 'エリー・コニファー', kind: 'character', n: 63 },
    ],
  },
];
// 無上層:還沒歸群上游的 tag,照樣能用能搜
export const MOCK_ROOTLESS: TreeNode[] = [
  { name: '原神', kind: 'copyright', n: 288 },
  { name: '戌亥とこ', kind: 'character', n: 74, multi: true },
  { name: '我不知道', kind: 'path', n: 3879 },
];
export const MOCK_FACETS = {
  general: [['1girl', 2980], ['smile', 1442], ['long_hair', 1330], ['solo', 1210]] as [string, number][],
  meta: [['2023', 8210], ['2024', 6120], ['2022', 3050]] as [string, number][],
};

/* ---------- 頂欄目前搜尋 token ---------- */
export interface SearchToken {
  text: string;
  kind: TagKind;
}
export const MOCK_SEARCH_TOKENS: SearchToken[] = [
  { text: '如月れん', kind: 'character' },
  { text: '2434', kind: 'copyright' },
];
export const MOCK_HIT_COUNT = 3184;
export const MOCK_WD14_QUEUE = 412;

/* ---------- 收藏的搜尋 ---------- */
export interface SavedSearch {
  title: string;
  query: string;
  special?: boolean;
  hits: number;
}
export const MOCK_SAVED: SavedSearch[] = [
  { title: '可能是個人照片', query: 'has:exif OR tag:realistic', special: true, hits: 842 },
  { title: '高人氣 · 如月れん', query: '如月れん rating:s -tag:lowres', hits: 1280 },
  { title: '待補作品的圖', query: '-copyright:* tag:1girl', hits: 503 },
  { title: '2434 · 2023', query: '2434 2023', hits: 3184 },
];

/* ---------- 匯入確認:路徑段 → tag ---------- */
export type ImportAction = 'map' | 'ignore' | 'year';
export interface ImportRow {
  seg: string;
  n: number;
  ex: string;
  action: ImportAction;
  cat?: TagKind;
  tag?: string;
}
export const MOCK_IMPORT: ImportRow[] = [
  { seg: '2434', n: 1204, ex: '…/2434/にじさんじ/如月れん.png', action: 'map', cat: 'copyright', tag: '2434' },
  { seg: '我不知道', n: 3879, ex: '…/我不知道/img_0421.jpg', action: 'ignore' },
  { seg: '2023', n: 8210, ex: '…/2023/夏コミ/…', action: 'year' },
  { seg: 'vspo', n: 642, ex: '…/vspo/花京院ちえり/…', action: 'map', cat: 'copyright', tag: 'VSpo!' },
  { seg: '如月れん', n: 88, ex: '…/2434/如月れん/0007.png', action: 'map', cat: 'character', tag: '如月れん' },
];
export const MOCK_IMPORT_SOURCE = '舊 GDrive';

/* ---------- 失蹤待辦匣 ---------- */
export interface ReconRow {
  title: string;
  seed: number;
  last: string;
}
export const MOCK_RECON: ReconRow[] = [
  { title: '如月れん_01.png', seed: 11, last: '本機 · pics\\2023\\2434\\如月れん_01.png · 3 天前' },
  { title: 'img_1042.jpg', seed: 5, last: '舊 GDrive · G\\圖\\未整理\\img_1042.jpg · 8 天前' },
  { title: '戌亥とこ.png', seed: 19, last: '新硬碟 · E\\art\\2024\\戌亥とこ.png · 1 天前' },
];
export const MOCK_RECON_RELOCATED = 214;

/* ---------- 圖庫來源 ---------- */
export interface RootRow {
  name: string;
  path: string;
  status: 'present' | 'warn';
  files: number;
  scan: string;
}
export const MOCK_ROOTS: RootRow[] = [
  { name: '本機', path: 'D:\\picture-management\\pics', status: 'present', files: 84210, scan: '12 分鐘前' },
  { name: '新硬碟', path: 'E:\\art', status: 'present', files: 9540, scan: '12 分鐘前' },
  { name: '舊 GDrive', path: 'G:\\我的雲端硬碟\\圖', status: 'warn', files: 41003, scan: '2 天前 · 38 個新段待確認' },
];
export const MOCK_ROOT_STATUS: { name: string; n: number; warn?: boolean }[] = [
  { name: '本機', n: 84210 },
  { name: '新硬碟', n: 9540 },
  { name: '舊 GDrive', n: 41003, warn: true },
];
