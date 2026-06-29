// §6.1 booru 分色:tag.kind → 顏色。
// 顏色唯一真相源是 styles.css @theme 的 --color-t-*(及 --color-danger);此處只「單向引用」
// token,不再手抄 hex(避免雙真相源)。回傳的是 CSS var() 字串,直接吃進 inline style。
export type TagKind =
  | 'character' | 'copyright' | 'expression' | 'general' | 'meta' | 'path' | 'manual';

const KINDS: ReadonlySet<string> = new Set<TagKind>([
  'character', 'copyright', 'expression', 'general', 'meta', 'path', 'manual',
]);

/** tag.kind → var(--color-t-*);未知 kind 退回 general。 */
export const tagColor = (kind: string): string =>
  `var(--color-t-${KINDS.has(kind) ? kind : 'general'})`;

/** 危險動作色(原為手抄 hex,改走 token)。 */
export const DANGER = 'var(--color-danger)';

/** 任意 CSS 顏色(含 var())+ 透明度 → 半透明底色/邊框。
 *  以 color-mix 取代舊 hexToRgba,與 .btn-danger / .note 既有慣例一致;
 *  底色不透明時視覺等價於 rgba(該色, a)。a ∈ [0,1]。 */
export const tint = (color: string, a: number): string =>
  `color-mix(in srgb, ${color} ${+(a * 100).toFixed(2)}%, transparent)`;

export const KIND_LABEL: Record<TagKind, string> = {
  character: '角色',
  copyright: '作品',
  expression: '表情',
  general: '屬性',
  meta: '年份／其他',
  path: '資料夾',
  manual: '我的標籤',
};
