// facet 側欄分區收折狀態的 localStorage 持久化(純函式,易測)。
export type FacetSection = 'dag' | 'general' | 'meta';
const KEY = 'pm.facet.collapsed';
const VALID: readonly FacetSection[] = ['dag', 'general', 'meta'];

export function loadCollapsed(storage: Storage): Set<FacetSection> {
  try {
    const raw = storage.getItem(KEY);
    if (!raw) return new Set();
    const arr = JSON.parse(raw) as unknown;
    if (!Array.isArray(arr)) return new Set();
    return new Set(arr.filter((s): s is FacetSection => VALID.includes(s as FacetSection)));
  } catch {
    return new Set();
  }
}

export function saveCollapsed(storage: Storage, set: ReadonlySet<FacetSection>): void {
  try {
    storage.setItem(KEY, JSON.stringify([...set]));
  } catch {
    /* 配額/隱私模式:忽略 */
  }
}

export function toggleCollapsed(set: ReadonlySet<FacetSection>, section: FacetSection): Set<FacetSection> {
  const next = new Set(set);
  if (next.has(section)) next.delete(section);
  else next.add(section);
  return next;
}
