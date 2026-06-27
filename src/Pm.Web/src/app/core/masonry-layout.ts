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
