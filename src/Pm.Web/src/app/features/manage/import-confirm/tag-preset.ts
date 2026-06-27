// import-confirm 常用 tag preset 的 localStorage 持久化(純函式,易測)。對齊 facet-collapse.ts 模式。
import type { TagKind } from '@core/tag-color';

export interface TagPreset { name: string; kind: TagKind; }

const KEY = 'pm.import.presets';
const VALID_KINDS: readonly TagKind[] = ['character', 'copyright', 'general', 'meta', 'path', 'manual'];

function isPreset(x: unknown): x is TagPreset {
  if (typeof x !== 'object' || x === null) return false;
  const p = x as { name?: unknown; kind?: unknown };
  return typeof p.name === 'string' && p.name.length > 0 && VALID_KINDS.includes(p.kind as TagKind);
}

export function loadPresets(storage: Storage): TagPreset[] {
  try {
    const raw = storage.getItem(KEY);
    if (!raw) return [];
    const arr = JSON.parse(raw) as unknown;
    if (!Array.isArray(arr)) return [];
    return arr.filter(isPreset);
  } catch {
    return [];
  }
}

export function savePresets(storage: Storage, presets: readonly TagPreset[]): void {
  try {
    storage.setItem(KEY, JSON.stringify(presets));
  } catch {
    /* 配額/隱私模式:忽略 */
  }
}

// 新增 preset(同名同 kind 視為重複,不重加);回新陣列。
export function addPreset(presets: readonly TagPreset[], p: TagPreset): TagPreset[] {
  if (presets.some((x) => x.name === p.name && x.kind === p.kind)) return [...presets];
  return [...presets, p];
}

export function removePreset(presets: readonly TagPreset[], p: TagPreset): TagPreset[] {
  return presets.filter((x) => !(x.name === p.name && x.kind === p.kind));
}
