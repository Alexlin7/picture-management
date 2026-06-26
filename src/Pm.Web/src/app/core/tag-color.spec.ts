import { hexToRgba } from './tag-color';

describe('hexToRgba', () => {
  it('6 碼 hex → rgba', () => {
    expect(hexToRgba('#3b82f6', 0.12)).toBe('rgba(59,130,246,0.12)');
    expect(hexToRgba('#FFFFFF', 1)).toBe('rgba(255,255,255,1)');
  });
  it('非 hex 輸入不產生 NaN(防呆退回黑色)', () => {
    expect(hexToRgba('#zzz', 0.5)).not.toContain('NaN');
  });
});
