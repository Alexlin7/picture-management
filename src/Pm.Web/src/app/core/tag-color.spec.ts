import { tagColor, tint, DANGER } from './tag-color';

describe('tagColor', () => {
  it('已知 kind → 對應 --color-t-* token', () => {
    expect(tagColor('character')).toBe('var(--color-t-character)');
    expect(tagColor('expression')).toBe('var(--color-t-expression)');
  });
  it('未知 kind → 退回 general', () => {
    expect(tagColor('???')).toBe('var(--color-t-general)');
  });
  it('DANGER 走 token,不再手抄 hex', () => {
    expect(DANGER).toBe('var(--color-danger)');
  });
});

describe('tint', () => {
  it('CSS 顏色 + alpha → color-mix(透明階對齊 a*100%)', () => {
    expect(tint('var(--color-t-meta)', 0.12)).toBe('color-mix(in srgb, var(--color-t-meta) 12%, transparent)');
    expect(tint('var(--color-t-meta)', 0.34)).toBe('color-mix(in srgb, var(--color-t-meta) 34%, transparent)');
  });
  it('避免浮點雜訊(0.4 → 40% 而非 40.000…%)', () => {
    expect(tint(DANGER, 0.4)).toBe('color-mix(in srgb, var(--color-danger) 40%, transparent)');
  });
});
