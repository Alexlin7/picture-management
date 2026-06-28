// 斷點單一真相源(TS 常數;CSS @media 不能吃 var())。
export const INSPECTOR_COLLAPSE = 1180; // stage 寬 < 此 → inspector 自動收
export const FACET_COLLAPSE = 940;      // stage 寬 < 此 → facet / 資料夾樹 自動收
export const MOBILE = 768;              // stage 寬 < 此 → 手機抽屜模式(兩側板改覆蓋式抽屜)
export const MASONRY_GAP = 12;
export const MIN_COL_WIDTH = { dense: 150, standard: 180, large: 280 } as const;

/** stage 寬量到(>0)且小於門檻 → 該自動收。未量到(<=0)視為不收,避免初始抖動。 */
export function shouldAutoCollapse(stageWidth: number, threshold: number): boolean {
  return stageWidth > 0 && stageWidth < threshold;
}
