// /tags 排序狀態的 localStorage 持久化(純函式,易測)。對齊 facet-collapse.ts 模式。
import type { SortKey, SortDir } from './tags.store';

const KEY = 'pm.tags.sort';
const VALID_KEYS: readonly SortKey[] = ['count', 'name', 'kind', 'recent'];
const VALID_DIRS: readonly SortDir[] = ['asc', 'desc'];
const DEFAULT: { key: SortKey; dir: SortDir } = { key: 'count', dir: 'desc' };

export function loadTagsSort(storage: Storage): { key: SortKey; dir: SortDir } {
  try {
    const raw = storage.getItem(KEY);
    if (!raw) return { ...DEFAULT };
    const obj = JSON.parse(raw) as unknown;
    if (typeof obj !== 'object' || obj === null) return { ...DEFAULT };
    const { key, dir } = obj as { key?: unknown; dir?: unknown };
    if (!VALID_KEYS.includes(key as SortKey) || !VALID_DIRS.includes(dir as SortDir)) {
      return { ...DEFAULT };
    }
    return { key: key as SortKey, dir: dir as SortDir };
  } catch {
    return { ...DEFAULT };
  }
}

export function saveTagsSort(storage: Storage, key: SortKey, dir: SortDir): void {
  try {
    storage.setItem(KEY, JSON.stringify({ key, dir }));
  } catch {
    /* 配額/隱私模式:忽略 */
  }
}
