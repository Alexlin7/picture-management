import { loadCollapsed, saveCollapsed, toggleCollapsed, type FacetSection } from './facet-collapse';

class MemStorage {
  private m = new Map<string, string>();
  getItem(k: string) { return this.m.get(k) ?? null; }
  setItem(k: string, v: string) { this.m.set(k, v); }
  removeItem(k: string) { this.m.delete(k); }
  clear() { this.m.clear(); }
  key() { return null; }
  get length() { return this.m.size; }
}

describe('facet-collapse', () => {
  it('save then load round-trips collapsed sections', () => {
    const s = new MemStorage() as unknown as Storage;
    saveCollapsed(s, new Set<FacetSection>(['general', 'meta']));
    expect([...loadCollapsed(s)].sort()).toEqual(['general', 'meta']);
  });

  it('load returns empty set when nothing stored', () => {
    const s = new MemStorage() as unknown as Storage;
    expect(loadCollapsed(s).size).toBe(0);
  });

  it('load ignores malformed / unknown values', () => {
    const s = new MemStorage() as unknown as Storage;
    s.setItem('pm.facet.collapsed', '["general","bogus","123"]');
    expect([...loadCollapsed(s)]).toEqual(['general']);
  });

  it('toggle adds then removes', () => {
    const a = toggleCollapsed(new Set(), 'dag');
    expect(a.has('dag')).toBe(true);
    const b = toggleCollapsed(a, 'dag');
    expect(b.has('dag')).toBe(false);
  });
});
