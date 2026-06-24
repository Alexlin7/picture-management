import { normalizeTagQuery, toggleExclude, exactMatch, reverseDisplayLookup } from './tag-search';
import type { TagListRow } from '@core/api/pm-api';

describe('normalizeTagQuery', () => {
  it('多字作品名空白轉底線', () => {
    expect(normalizeTagQuery('blue archive')).toBe('blue_archive');
  });
  it('去排除前綴 + 頭尾空白 + 轉小寫', () => {
    expect(normalizeTagQuery('  -Blue Archive ')).toBe('blue_archive');
  });
  it('連續空白收成單一底線', () => {
    expect(normalizeTagQuery('long   hair')).toBe('long_hair');
  });
  it('純空白回空字串', () => {
    expect(normalizeTagQuery('   ')).toBe('');
  });
});

describe('toggleExclude', () => {
  it('無前綴 → 加 -', () => {
    expect(toggleExclude('smile')).toBe('-smile');
  });
  it('有前綴 → 去 -', () => {
    expect(toggleExclude('-smile')).toBe('smile');
  });
});

const rows: TagListRow[] = [
  { id: 1, name: 'smile', kind: 'general', count: 9 },
  { id: 2, name: 'blue_archive', kind: 'copyright', count: 3 },
];

describe('exactMatch', () => {
  it('多字輸入正規化後精準命中', () => {
    expect(exactMatch(rows, 'Blue Archive')?.id).toBe(2);
  });
  it('非完全相等 → null', () => {
    expect(exactMatch(rows, 'smil')).toBeNull();
  });
  it('空輸入 → null', () => {
    expect(exactMatch(rows, '  ')).toBeNull();
  });
});

describe('reverseDisplayLookup', () => {
  it('中文顯示名反查回 canonical', () => {
    expect(reverseDisplayLookup('微笑')).toContain('smile');
  });
  it('片段可命中多個 label', () => {
    // '笑' 命中 微笑/咧嘴笑/淺笑… 至少 2 個
    expect(reverseDisplayLookup('笑').length).toBeGreaterThan(1);
  });
  it('空片段 → 空陣列', () => {
    expect(reverseDisplayLookup('  ')).toEqual([]);
  });
});
