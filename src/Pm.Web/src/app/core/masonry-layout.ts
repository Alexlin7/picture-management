// src/Pm.Web/src/app/core/masonry-layout.ts
export interface MasonryBox { left: number; top: number; width: number; height: number; }
export interface MasonryLayout { cols: number; colWidth: number; boxes: MasonryBox[]; containerHeight: number; }

/** 依容器寬與各格長寬比算瀑布流座標。最少 1 欄,永不破版;以已知 aspect 直接算高,不量 DOM。 */
export function computeMasonryLayout(
  containerWidth: number, aspects: number[], minColWidth: number, gap: number): MasonryLayout {
  if (containerWidth <= 0) return { cols: 0, colWidth: 0, boxes: [], containerHeight: 0 };

  const cols = Math.max(1, Math.floor((containerWidth + gap) / (minColWidth + gap)));
  const colWidth = (containerWidth - gap * (cols - 1)) / cols;
  const colHeights = new Array<number>(cols).fill(0);

  const boxes: MasonryBox[] = aspects.map((aspect) => {
    const a = aspect > 0 ? aspect : 1;
    const height = colWidth / a;
    let c = 0;
    for (let i = 1; i < cols; i++) if (colHeights[i] < colHeights[c]) c = i;
    const left = c * (colWidth + gap);
    const top = colHeights[c];
    colHeights[c] = top + height + gap;
    return { left, top, width: colWidth, height };
  });

  const tallest = colHeights.reduce((m, h) => (h > m ? h : m), 0);
  const containerHeight = boxes.length ? tallest - gap : 0;
  return { cols, colWidth, boxes, containerHeight };
}

/** windowing 判斷:box 是否落在 [scrollTop-overscan, scrollTop+vpHeight+overscan] 視窗內。
 *  純函式,給 Masonry virtual scroll 過濾用(JSDOM 量不到 DOM,故抽出可單測)。 */
export function isBoxInWindow(
  box: MasonryBox, scrollTop: number, vpHeight: number, overscan: number): boolean {
  const min = scrollTop - overscan;
  const max = scrollTop + vpHeight + overscan;
  return box.top + box.height >= min && box.top <= max;
}

export type GridNavDir = 'left' | 'right' | 'up' | 'down';

/** roving tabindex 方向鍵導航:依 box 幾何算下一個焦點 index。
 *  left/right = 閱讀順序前後一格;up/down = 同欄(centerX 接近)最近的上/下一格
 *  —— 用幾何而非 index±cols,才能正確處理瀑布流的不定高與欄高不齊。
 *  無處可去(邊界)或 current 越界時回原 index(不動)。 */
export function gridNavTarget(boxes: MasonryBox[], current: number, dir: GridNavDir): number {
  if (current < 0 || current >= boxes.length) return current;
  if (dir === 'left') return current > 0 ? current - 1 : current;
  if (dir === 'right') return current < boxes.length - 1 ? current + 1 : current;

  const cur = boxes[current];
  const curCenter = cur.left + cur.width / 2;
  const tol = cur.width * 0.5; // 同欄容差:中心 x 差 < 半個欄寬視為同欄
  let best = current;
  let bestTop = dir === 'down' ? Infinity : -Infinity;
  for (let i = 0; i < boxes.length; i++) {
    if (i === current) continue;
    const b = boxes[i];
    if (Math.abs(b.left + b.width / 2 - curCenter) > tol) continue; // 非同欄
    if (dir === 'down') {
      if (b.top > cur.top && b.top < bestTop) { bestTop = b.top; best = i; }
    } else {
      if (b.top < cur.top && b.top > bestTop) { bestTop = b.top; best = i; }
    }
  }
  return best;
}
