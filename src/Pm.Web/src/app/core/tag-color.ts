// §6.1 booru 分色:tag.kind → 顏色
export const TAG_COLOR: Record<string, string> = {
  character: '#4ADE80',
  copyright: '#C084FC',
  expression: '#FB7185', // 表情:顯示層合成 group,從 general 拉出
  general:   '#818CF8',
  meta:      '#FBBF24',
  path:      '#94A3B8',
  manual:    '#F472B6',
};
export const ACCENT = '#22D3EE';
export const DANGER = '#F0616D';
export const tagColor = (kind: string) => TAG_COLOR[kind] ?? TAG_COLOR['general'];

export type TagKind = keyof typeof TAG_COLOR; // character|copyright|expression|general|meta|path|manual
export const KIND_LABEL: Record<TagKind, string> = {
  character: '角色',
  copyright: '作品',
  expression: '表情',
  general: '屬性',
  meta: '年份／其他',
  path: '資料夾',
  manual: '我的標籤',
};
