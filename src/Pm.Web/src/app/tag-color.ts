// §6.1 booru 分色:tag.kind → 顏色
export const TAG_COLOR: Record<string, string> = {
  character: '#4ADE80',
  copyright: '#C084FC',
  general:   '#818CF8',
  meta:      '#FBBF24',
  path:      '#94A3B8',
  manual:    '#F472B6',
};
export const ACCENT = '#22D3EE';
export const DANGER = '#F0616D';
export const tagColor = (kind: string) => TAG_COLOR[kind] ?? TAG_COLOR['general'];
