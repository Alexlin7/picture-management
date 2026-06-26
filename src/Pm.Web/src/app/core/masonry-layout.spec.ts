// src/Pm.Web/src/app/core/masonry-layout.spec.ts
import { computeMasonryLayout } from './masonry-layout';

describe('computeMasonryLayout', () => {
  it('never returns 0 columns for positive width', () => {
    expect(computeMasonryLayout(100, [1], 180, 12).cols).toBe(1);
  });

  it('column count grows with width', () => {
    const gap = 12, min = 180;
    expect(computeMasonryLayout(180, [1, 1], min, gap).cols).toBe(1);
    expect(computeMasonryLayout(372, [1, 1], min, gap).cols).toBe(2); // (372+12)/(180+12)=2
    expect(computeMasonryLayout(600, [1, 1], min, gap).cols).toBe(3);
  });

  it('computes column width accounting for gaps', () => {
    const l = computeMasonryLayout(372, [1, 1], 180, 12); // 2 cols
    expect(l.colWidth).toBeCloseTo((372 - 12) / 2); // 180
  });

  it('places items into shortest column (greedy)', () => {
    // 2 cols, all aspect 1 → square boxes; 3rd item goes back to col0.
    const l = computeMasonryLayout(372, [1, 1, 1], 180, 12);
    expect(l.boxes[0].left).toBeCloseTo(0);
    expect(l.boxes[1].left).toBeCloseTo(180 + 12);
    expect(l.boxes[2].left).toBeCloseTo(0);
    expect(l.boxes[2].top).toBeCloseTo(180 + 12);
  });

  it('uses 1:1 fallback for non-positive aspect', () => {
    const l = computeMasonryLayout(372, [0], 180, 12);
    expect(l.boxes[0].height).toBeCloseTo(180); // colWidth / 1
  });

  it('returns empty layout for non-positive width', () => {
    expect(computeMasonryLayout(0, [1], 180, 12)).toEqual({ cols: 0, colWidth: 0, boxes: [], containerHeight: 0 });
  });
});
