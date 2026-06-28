// src/Pm.Web/src/app/core/masonry-layout.spec.ts
import { computeMasonryLayout, isBoxInWindow, gridNavTarget, type MasonryBox } from './masonry-layout';

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

describe('isBoxInWindow', () => {
  const box = (top: number, height: number): MasonryBox => ({ left: 0, top, width: 100, height });

  it('includes a box fully inside the viewport', () => {
    // viewport [0, 500], overscan 0
    expect(isBoxInWindow(box(100, 50), 0, 500, 0)).toBe(true);
  });

  it('excludes a box far below the viewport + overscan', () => {
    // window = [-600, 500+600=1100]; box top 2000 is out
    expect(isBoxInWindow(box(2000, 50), 0, 500, 600)).toBe(false);
  });

  it('excludes a box fully scrolled past above the window', () => {
    // scrollTop 2000 → window [1400, 2600]; box ending at 100 is out
    expect(isBoxInWindow(box(50, 50), 2000, 500, 600)).toBe(false);
  });

  it('includes a box within the overscan band just above the viewport', () => {
    // scrollTop 1000 → window [400, 2100] (overscan 600); box at top 500 included
    expect(isBoxInWindow(box(500, 50), 1000, 500, 600)).toBe(true);
  });

  it('includes a box straddling the viewport top edge', () => {
    // scrollTop 1000, overscan 0 → window [1000, 1500]; box [980, 1030] straddles top
    expect(isBoxInWindow(box(980, 50), 1000, 500, 0)).toBe(true);
  });
});

describe('gridNavTarget', () => {
  // 2 欄瀑布流(欄寬 100、gap 10):col0 left=0(center 50),col1 left=110(center 160)
  //   index 0: col0 top 0   h100
  //   index 1: col1 top 0   h50
  //   index 2: col1 top 60  h100
  //   index 3: col0 top 110 h80
  const boxes: MasonryBox[] = [
    { left: 0, top: 0, width: 100, height: 100 },
    { left: 110, top: 0, width: 100, height: 50 },
    { left: 110, top: 60, width: 100, height: 100 },
    { left: 0, top: 110, width: 100, height: 80 },
  ];

  it('right/left 走閱讀順序前後一格', () => {
    expect(gridNavTarget(boxes, 0, 'right')).toBe(1);
    expect(gridNavTarget(boxes, 1, 'left')).toBe(0);
  });

  it('down 走同欄最近的下一格(依幾何,非 index+cols)', () => {
    expect(gridNavTarget(boxes, 0, 'down')).toBe(3); // col0:0 → 3
    expect(gridNavTarget(boxes, 1, 'down')).toBe(2); // col1:1 → 2
  });

  it('up 走同欄最近的上一格', () => {
    expect(gridNavTarget(boxes, 3, 'up')).toBe(0); // col0:3 → 0
    expect(gridNavTarget(boxes, 2, 'up')).toBe(1); // col1:2 → 1
  });

  it('邊界無處可去時回原 index', () => {
    expect(gridNavTarget(boxes, 0, 'left')).toBe(0);  // 第一格往左
    expect(gridNavTarget(boxes, 3, 'right')).toBe(3); // 最後一格往右
    expect(gridNavTarget(boxes, 0, 'up')).toBe(0);    // col0 最上往上
    expect(gridNavTarget(boxes, 3, 'down')).toBe(3);  // col0 最下往下
  });

  it('current 越界回原值', () => {
    expect(gridNavTarget(boxes, -1, 'right')).toBe(-1);
    expect(gridNavTarget(boxes, 9, 'down')).toBe(9);
  });
});
