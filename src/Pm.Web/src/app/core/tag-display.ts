// WD14 tag 顯示層(v1)— 純函式,無副作用,gallery / inspector 共用。
// 設計:docs/design/2026-06-22-tag-display-layer-design.md §④(已鎖定)
//       資料:docs/design/2026-06-23-tag-display-v1-dataprep.md
// 鐵則:canonical(tag.name)是唯一真相,照存照搜;本層只做顯示裝飾與重新分組,
//       不改資料、不影響查詢。對照表查不到一律優雅退回(底線轉空白)。

// canonical → 顯示資訊。group 覆寫 kind 決定分區。
export const EXPRESSION_DISPLAY_MAP: Record<
  string,
  { label: string; emoji: string; group: 'expression' }
> = {
  blush: { label: '臉紅', emoji: '😊', group: 'expression' },
  smile: { label: '微笑', emoji: '🙂', group: 'expression' },
  open_mouth: { label: '張嘴', emoji: '😮', group: 'expression' },
  closed_mouth: { label: '閉嘴', emoji: '😐', group: 'expression' },
  closed_eyes: { label: '閉眼', emoji: '😌', group: 'expression' },
  ':d': { label: '張嘴笑', emoji: '😃', group: 'expression' },
  sweat: { label: '流汗', emoji: '😅', group: 'expression' },
  parted_lips: { label: '微張唇', emoji: '😶', group: 'expression' },
  teeth: { label: '露齒', emoji: '😬', group: 'expression' },
  one_eye_closed: { label: '閉單眼', emoji: '😉', group: 'expression' },
  tongue: { label: '露舌', emoji: '👅', group: 'expression' },
  fang: { label: '虎牙', emoji: '🦷', group: 'expression' },
  tongue_out: { label: '吐舌', emoji: '😛', group: 'expression' },
  tears: { label: '眼淚', emoji: '😢', group: 'expression' },
  grin: { label: '咧嘴笑', emoji: '😁', group: 'expression' },
  sweatdrop: { label: '汗滴', emoji: '💦', group: 'expression' },
  ':o': { label: '張嘴', emoji: '😮', group: 'expression' },
  lips: { label: '嘴唇', emoji: '👄', group: 'expression' },
  saliva: { label: '口水', emoji: '💧', group: 'expression' },
  ':3': { label: '貓嘴', emoji: '😺', group: 'expression' },
  nose_blush: { label: '鼻頭臉紅', emoji: '😳', group: 'expression' },
  '^_^': { label: '瞇眼笑', emoji: '😄', group: 'expression' },
  expressionless: { label: '面無表情', emoji: '😑', group: 'expression' },
  frown: { label: '皺眉', emoji: '☹️', group: 'expression' },
  embarrassed: { label: '尷尬', emoji: '😖', group: 'expression' },
  blush_stickers: { label: '臉紅貼貼', emoji: '😊', group: 'expression' },
  'half-closed_eyes': { label: '半閉眼', emoji: '😪', group: 'expression' },
  happy: { label: '開心', emoji: '😄', group: 'expression' },
  mouth_hold: { label: '嘴叼', emoji: '', group: 'expression' },
  wavy_mouth: { label: '波浪嘴', emoji: '😣', group: 'expression' },
  trembling: { label: '顫抖', emoji: '😨', group: 'expression' },
  crying: { label: '哭', emoji: '😭', group: 'expression' },
  sharp_teeth: { label: '尖牙', emoji: '🦷', group: 'expression' },
  light_smile: { label: '淺笑', emoji: '🙂', group: 'expression' },
  ';d': { label: '眨眼張嘴笑', emoji: '😆', group: 'expression' },
  '>_<': { label: '用力閉眼', emoji: '😆', group: 'expression' },
  clenched_teeth: { label: '咬牙', emoji: '😬', group: 'expression' },
  drooling: { label: '流口水', emoji: '🤤', group: 'expression' },
  surprised: { label: '驚訝', emoji: '😲', group: 'expression' },
  anger_vein: { label: '青筋', emoji: '💢', group: 'expression' },
  angry: { label: '生氣', emoji: '😠', group: 'expression' },
  ':<': { label: '嘟嘴', emoji: '😟', group: 'expression' },
  tearing_up: { label: '泛淚', emoji: '🥹', group: 'expression' },
  ':p': { label: '吐舌', emoji: '😝', group: 'expression' },
  one_eye_covered: { label: '單眼被遮', emoji: '', group: 'expression' },
  crying_with_eyes_open: { label: '睜眼哭', emoji: '😢', group: 'expression' },
  '@_@': { label: '暈眩眼', emoji: '😵', group: 'expression' },
  ':q': { label: '舔舌', emoji: '😋', group: 'expression' },
  naughty_face: { label: '壞笑', emoji: '😏', group: 'expression' },
  'wide-eyed': { label: '睜大眼', emoji: '😳', group: 'expression' },
  serious: { label: '嚴肅', emoji: '😐', group: 'expression' },
  '=_=': { label: '無奈瞇眼', emoji: '😑', group: 'expression' },
  smirk: { label: '得意斜笑', emoji: '😏', group: 'expression' },
  ':t': { label: '嘟嘴', emoji: '😤', group: 'expression' },
  ';)': { label: '眨眼', emoji: '😉', group: 'expression' },
  pout: { label: '嘟嘴', emoji: '😗', group: 'expression' },
  'full-face_blush': { label: '滿臉通紅', emoji: '😳', group: 'expression' },
  ahegao: { label: '阿黑顏', emoji: '😵', group: 'expression' },
  smug: { label: '自滿', emoji: '😏', group: 'expression' },
  wince: { label: '苦相', emoji: '😖', group: 'expression' },
  laughing: { label: '大笑', emoji: '😆', group: 'expression' },
  '>:)': { label: '邪笑', emoji: '😈', group: 'expression' },
  evil_smile: { label: '邪笑', emoji: '😈', group: 'expression' },
  scared: { label: '害怕', emoji: '😱', group: 'expression' },
  rolling_eyes: { label: '翻白眼', emoji: '🙄', group: 'expression' },
  annoyed: { label: '煩躁', emoji: '😤', group: 'expression' },
  sad: { label: '難過', emoji: '😞', group: 'expression' },
  nervous: { label: '緊張', emoji: '😰', group: 'expression' },
  sleepy: { label: '想睡', emoji: '😴', group: 'expression' },
  shy: { label: '害羞', emoji: '☺️', group: 'expression' },
  glaring: { label: '怒視', emoji: '😠', group: 'expression' },
};

// 限定詞(qualifier)黑名單:角色標單一尾端括號若命中,歸「造型」而非「作品」,
// 避免渲染 ‹作品: male› 這類誤導徽章。只在判定 work/costume 歸屬時查;不影響 canonical / 搜尋。
// 注意:swimsuit/rider/lancer/maid/bunny 等多為真造型,刻意不入黑名單(spec §④)。
export const NON_WORK_SUFFIX: ReadonlySet<string> = new Set([
  'male', 'female', 'young', 'old', 'aged_up', 'child', 'teenage', 'adult',
  'alternate', 'cosplay', 'ghost', 'human', 'beast',
]);

// 底線轉空白(通用基底)。
export const spaces = (s: string): string => s.replaceAll('_', ' ');

// character 標的結構解析結果(RAW:底線保留,由 displayOf 套 spaces)。
export interface CharacterParse {
  name: string;
  costumes: string[];
  work: string | null;
}

// 反覆剝離尾端 `_(<不含括號內容>)` 群組;最後一組非黑名單者=作品,其餘=造型。
// 嚴禁把 : / - ' 當分隔(regex 內容類別涵蓋所有非括號字元)。
// 剝完前段為空(如 `_(foo)`)→ 回 null,讓 displayOf 退回底線轉空白。
const SUFFIX_RE = /_\(([^()]*)\)$/;

export function parseCharacter(name: string): CharacterParse | null {
  const groups: string[] = [];
  let rest = name;
  let m: RegExpMatchArray | null;
  while ((m = rest.match(SUFFIX_RE)) !== null) {
    groups.unshift(m[1]); // unshift → 還原為由左到右順序
    rest = rest.slice(0, m.index);
  }
  if (rest.length === 0) return null; // 畸形:括號前無角色名
  if (groups.length === 0) return { name: rest, costumes: [], work: null };

  // work = 最右側「非黑名單」群組;若全為黑名單則無作品。其餘群組(順序保留)皆為造型。
  let workIdx = -1;
  for (let i = groups.length - 1; i >= 0; i--) {
    if (!NON_WORK_SUFFIX.has(groups[i])) {
      workIdx = i;
      break;
    }
  }
  const work = workIdx >= 0 ? groups[workIdx] : null;
  const costumes = groups.filter((_, i) => i !== workIdx);
  return { name: rest, costumes, work };
}

// displayOf 的輸入:photo detail 的一個 tag(canonical + kind + 來源/信心度)。
export interface TagInput {
  name: string;
  kind: string;
  source?: string;
  confidence?: number | null;
}

// 顯示模型。group 決定分區(對照表可覆寫,否則用 kind)。
// costumes/work 僅 character 解析命中時存在。
export interface TagDisplay {
  canonical: string;
  source?: string;
  confidence: number | null;
  label: string;
  emoji: string | null;
  group: string;
  costumes?: string[];
  work?: string | null;
}

export function displayOf(tag: TagInput): TagDisplay {
  const base = {
    canonical: tag.name,
    source: tag.source,
    confidence: tag.confidence ?? null,
  };

  // ② 對照表(以 canonical 為鍵)
  const entry = EXPRESSION_DISPLAY_MAP[tag.name];
  if (entry) {
    return {
      ...base,
      label: entry.label,
      emoji: entry.emoji || null, // 空字串=無貼切 emoji → null
      group: entry.group, // 覆寫 kind
    };
  }

  // ④ character 括號 kind-aware 解析
  if (tag.kind === 'character') {
    const parsed = parseCharacter(tag.name);
    if (parsed) {
      return {
        ...base,
        group: 'character',
        label: spaces(parsed.name),
        emoji: null,
        costumes: parsed.costumes.map(spaces),
        work: parsed.work ? spaces(parsed.work) : null,
      };
    }
  }

  // ① 退回:底線轉空白
  return { ...base, label: spaces(tag.name), emoji: null, group: tag.kind };
}

// 檢視器分區的固定顯示順序。其餘 group(source-as-kind 的 manual/path,或未知)
// 附在這些之後,確保任何 tag 都不會被分組邏輯丟掉。
export const DISPLAY_GROUP_ORDER = ['character', 'copyright', 'expression', 'general', 'meta'] as const;

export type DisplayTag = TagDisplay & { id: number };
export interface TagLane {
  group: string;
  tags: DisplayTag[];
}

// 把 photo 的 tags 經 displayOf 分組成 lanes:已知 display group 依 DISPLAY_GROUP_ORDER,
// 其餘 group 依首次出現序附在後(不丟 manual/path)。各 tag 帶回 id 供移除 / track。
export function groupTags(tags: Array<TagInput & { id: number }>): TagLane[] {
  const byGroup = new Map<string, DisplayTag[]>();
  for (const t of tags) {
    const d: DisplayTag = { ...displayOf(t), id: t.id };
    const bucket = byGroup.get(d.group);
    if (bucket) bucket.push(d);
    else byGroup.set(d.group, [d]);
  }
  const ordered: string[] = DISPLAY_GROUP_ORDER.filter((g) => byGroup.has(g));
  for (const g of byGroup.keys()) if (!ordered.includes(g)) ordered.push(g);
  return ordered.map((group) => ({ group, tags: byGroup.get(group)! }));
}
