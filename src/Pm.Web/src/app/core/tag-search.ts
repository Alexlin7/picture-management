// 搜尋用純函式(無副作用)。canonical 照存照搜;本檔只做「比對用」字串轉換與反查。

import type { TagListRow } from '@core/api/pm-api';
import { EXPRESSION_DISPLAY_MAP } from './tag-display';

// 使用者輸入 → 比對用字串:去 '-' 前綴、trim、轉小寫、內部連續空白收成單一 '_'。
export function normalizeTagQuery(input: string): string {
  return input
    .trim()
    .replace(/^-/, '')
    .trim()
    .toLowerCase()
    .replace(/\s+/g, '_');
}

// 切換排除前綴:無 '-' → 加;有 '-' → 去。
export function toggleExclude(text: string): string {
  return text.startsWith('-') ? text.slice(1) : '-' + text;
}

// 在既有建議中找「正規化後名稱完全相等」者(精準 Enter 用);無 → null。
export function exactMatch(rows: readonly TagListRow[], term: string): TagListRow | null {
  const t = normalizeTagQuery(term);
  if (!t) return null;
  return rows.find((r) => r.name.toLowerCase() === t) ?? null;
}

// 從建議中濾掉已選 token(token 的 '-' 排除前綴去掉再比 name;不分大小寫)。
export function excludeSelected(
  rows: readonly TagListRow[],
  tokenTexts: readonly string[],
): TagListRow[] {
  const selected = new Set(tokenTexts.map((t) => t.replace(/^-/, '').toLowerCase()));
  return rows.filter((r) => !selected.has(r.name.toLowerCase()));
}

// token → URL query 片段:text 內部空白轉 '_',多個以 '+' 串。空 → ''。
export function encodeTokens(tokens: readonly { text: string }[]): string {
  return tokens
    .map((t) => t.text.trim())
    .filter(Boolean)
    .map((t) => t.replace(/\s+/g, '_'))
    .join('+');
}

// URL query 片段 → token(kind 不進 URL,一律 general;空/壞 → [])。
export function decodeTokens(q: string): { text: string; kind: 'general' }[] {
  if (!q) return [];
  return q
    .split('+')
    .map((s) => s.replace(/_/g, ' ').trim())
    .filter(Boolean)
    .map((text) => ({ text, kind: 'general' as const }));
}

// 中文/顯示名反查:回傳「顯示 label 含此片段」的 canonical 英文 tag 名。
// 僅覆蓋 curated 顯示名(表情等);空片段或無命中 → []。
export function reverseDisplayLookup(term: string): string[] {
  const t = term.trim();
  if (!t) return [];
  const out: string[] = [];
  for (const [canonical, entry] of Object.entries(EXPRESSION_DISPLAY_MAP)) {
    if (entry.label.includes(t)) out.push(canonical);
  }
  return out;
}
