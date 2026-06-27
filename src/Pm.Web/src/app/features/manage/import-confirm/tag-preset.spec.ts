import { loadPresets, savePresets, addPreset, removePreset, type TagPreset } from './tag-preset';

class MemStorage {
  private m = new Map<string, string>();
  getItem(k: string) { return this.m.get(k) ?? null; }
  setItem(k: string, v: string) { this.m.set(k, v); }
  removeItem(k: string) { this.m.delete(k); }
  clear() { this.m.clear(); }
  key() { return null; }
  get length() { return this.m.size; }
}

const p = (name: string, kind: TagPreset['kind'] = 'general'): TagPreset => ({ name, kind });

describe('tag-preset', () => {
  it('save then load round-trips presets', () => {
    const s = new MemStorage() as unknown as Storage;
    savePresets(s, [p('blue'), p('akira', 'character')]);
    expect(loadPresets(s)).toEqual([p('blue'), p('akira', 'character')]);
  });

  it('load returns empty on nothing / malformed / invalid kind', () => {
    const s = new MemStorage() as unknown as Storage;
    expect(loadPresets(s)).toEqual([]);
    s.setItem('pm.import.presets', 'not-json');
    expect(loadPresets(s)).toEqual([]);
    s.setItem('pm.import.presets', JSON.stringify([{ name: 'x', kind: 'bogus' }, p('ok')]));
    expect(loadPresets(s)).toEqual([p('ok')]);
  });

  it('addPreset ignores exact duplicates but keeps same-name different-kind', () => {
    let list = addPreset([], p('blue'));
    list = addPreset(list, p('blue'));            // dup → no-op
    expect(list).toEqual([p('blue')]);
    list = addPreset(list, p('blue', 'character')); // same name, diff kind → added
    expect(list.length).toBe(2);
  });

  it('removePreset removes the matching entry', () => {
    const list = [p('blue'), p('akira', 'character')];
    expect(removePreset(list, p('blue'))).toEqual([p('akira', 'character')]);
  });
});
