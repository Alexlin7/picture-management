import { normalizeTagQuery, toggleExclude, exactMatch, reverseDisplayLookup, excludeSelected, encodeTokens, decodeTokens } from './tag-search';
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

const sugRows: TagListRow[] = [
  { id: 1, name: 'smile', kind: 'general', count: 9 },
  { id: 2, name: 'blue_archive', kind: 'copyright', count: 3 },
];

describe('excludeSelected', () => {
  it('濾掉已選為包含的標', () => {
    expect(excludeSelected(sugRows, ['smile']).map((r) => r.id)).toEqual([2]);
  });
  it('濾掉已選為排除的標(去 - 前綴比對)', () => {
    expect(excludeSelected(sugRows, ['-smile']).map((r) => r.id)).toEqual([2]);
  });
  it('不分大小寫', () => {
    expect(excludeSelected(sugRows, ['SMILE']).map((r) => r.id)).toEqual([2]);
  });
  it('無已選 → 全保留', () => {
    expect(excludeSelected(sugRows, []).map((r) => r.id)).toEqual([1, 2]);
  });
});

describe('encodeTokens / decodeTokens', () => {
  it('多 token 以 , 串、canonical text 原樣保留', () => {
    expect(encodeTokens([{ text: 'large_breasts' }, { text: '-smile' }])).toBe('large_breasts,-smile');
  });
  it('空陣列 → 空字串', () => {
    expect(encodeTokens([])).toBe('');
  });
  it('decode 還原為 text + general kind(底線不被改成空白)', () => {
    expect(decodeTokens('large_breasts,-smile')).toEqual([
      { text: 'large_breasts', kind: 'general' },
      { text: '-smile', kind: 'general' },
    ]);
  });
  it('含底線 canonical tag 往返為 identity(回歸:large_breasts 不可變 large breasts)', () => {
    const tokens = [{ text: 'large_breasts' }, { text: 'long_hair' }, { text: '-blue_archive' }];
    const decoded = decodeTokens(encodeTokens(tokens));
    expect(decoded.map((t) => t.text)).toEqual(['large_breasts', 'long_hair', '-blue_archive']);
  });
  it('空/壞值 → 空陣列', () => {
    expect(decodeTokens('')).toEqual([]);
    expect(decodeTokens(',,,')).toEqual([]);
  });
});
