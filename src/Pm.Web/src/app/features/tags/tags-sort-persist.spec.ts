import { loadTagsSort, saveTagsSort } from './tags-sort-persist';

class MemStorage {
  private m = new Map<string, string>();
  getItem(k: string) { return this.m.get(k) ?? null; }
  setItem(k: string, v: string) { this.m.set(k, v); }
  removeItem(k: string) { this.m.delete(k); }
  clear() { this.m.clear(); }
  key() { return null; }
  get length() { return this.m.size; }
}

describe('tags-sort-persist', () => {
  it('save then load round-trips key + dir', () => {
    const s = new MemStorage() as unknown as Storage;
    saveTagsSort(s, 'name', 'asc');
    expect(loadTagsSort(s)).toEqual({ key: 'name', dir: 'asc' });
  });

  it('load returns default when nothing stored', () => {
    const s = new MemStorage() as unknown as Storage;
    expect(loadTagsSort(s)).toEqual({ key: 'count', dir: 'desc' });
  });

  it('load returns default on malformed JSON', () => {
    const s = new MemStorage() as unknown as Storage;
    s.setItem('pm.tags.sort', 'not-json');
    expect(loadTagsSort(s)).toEqual({ key: 'count', dir: 'desc' });
  });

  it('load returns default on invalid key/dir', () => {
    const s = new MemStorage() as unknown as Storage;
    s.setItem('pm.tags.sort', JSON.stringify({ key: 'bogus', dir: 'asc' }));
    expect(loadTagsSort(s)).toEqual({ key: 'count', dir: 'desc' });
  });
});
