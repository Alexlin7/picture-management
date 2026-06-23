import {
  spaces,
  parseCharacter,
  displayOf,
  groupTags,
  EXPRESSION_DISPLAY_MAP,
  NON_WORK_SUFFIX,
} from './tag-display';

// 對照 specs:
//   docs/superpowers/specs/2026-06-22-tag-display-layer-design.md §④ + 測試考量
//   docs/superpowers/specs/2026-06-23-tag-display-v1-dataprep.md(44 筆真實語料)
// 不變式:canonical 照存照搜,顯示層只裝飾;parseCharacter 回 RAW(底線保留),
//         spaces() 由 displayOf 套用。

describe('spaces', () => {
  it('底線轉空白', () => {
    expect(spaces('long_hair')).toBe('long hair');
  });
  it('多個底線全部轉換', () => {
    expect(spaces('warrior_of_light')).toBe('warrior of light');
  });
  it('無底線維持原樣', () => {
    expect(spaces('cirno')).toBe('cirno');
  });
});

describe('parseCharacter（回 RAW,底線保留;work=最後一組非黑名單,其餘 costumes）', () => {
  // single-work
  it('角色_(作品)', () => {
    expect(parseCharacter('sensei_(blue_archive)')).toEqual({
      name: 'sensei', costumes: [], work: 'blue_archive',
    });
    expect(parseCharacter('ganyu_(genshin_impact)')).toEqual({
      name: 'ganyu', costumes: [], work: 'genshin_impact',
    });
  });

  // 多詞角色名(名字含底線,不可被當作品分隔)
  it('多詞角色名 + 作品', () => {
    expect(parseCharacter('hu_tao_(genshin_impact)')).toEqual({
      name: 'hu_tao', costumes: [], work: 'genshin_impact',
    });
    expect(parseCharacter('doodle_sensei_(blue_archive)')).toEqual({
      name: 'doodle_sensei', costumes: [], work: 'blue_archive',
    });
    expect(parseCharacter('warrior_of_light_(ff14)')).toEqual({
      name: 'warrior_of_light', costumes: [], work: 'ff14',
    });
  });

  // costume-work
  it('角色_(造型)_(作品)', () => {
    expect(parseCharacter('asuna_(bunny)_(blue_archive)')).toEqual({
      name: 'asuna', costumes: ['bunny'], work: 'blue_archive',
    });
    expect(parseCharacter('medusa_(rider)_(fate)')).toEqual({
      name: 'medusa', costumes: ['rider'], work: 'fate',
    });
  });

  // 三括號(多造型 + 作品)
  it('角色_(造型)_(造型)_(作品)', () => {
    expect(parseCharacter('artoria_pendragon_(alter_swimsuit_rider)_(second_ascension)_(fate)')).toEqual({
      name: 'artoria_pendragon',
      costumes: ['alter_swimsuit_rider', 'second_ascension'],
      work: 'fate',
    });
    expect(parseCharacter('meltryllis_(swimsuit_lancer)_(first_ascension)_(fate)')).toEqual({
      name: 'meltryllis',
      costumes: ['swimsuit_lancer', 'first_ascension'],
      work: 'fate',
    });
  });

  // 特殊字元不可當分隔:冒號
  it('作品名含冒號（不可當分隔）', () => {
    expect(parseCharacter('trailblazer_(honkai:_star_rail)')).toEqual({
      name: 'trailblazer', costumes: [], work: 'honkai:_star_rail',
    });
    expect(parseCharacter('rem_(re:zero)')).toEqual({
      name: 'rem', costumes: [], work: 're:zero',
    });
    expect(parseCharacter('2b_(nier:automata)')).toEqual({
      name: '2b', costumes: [], work: 'nier:automata',
    });
  });

  // 特殊字元:斜線
  it('作品名含斜線（不可當分隔）', () => {
    expect(parseCharacter('tamamo_no_mae_(fate/extra)')).toEqual({
      name: 'tamamo_no_mae', costumes: [], work: 'fate/extra',
    });
  });

  // 特殊字元:連字號（造型內）
  it('造型名含連字號（不可當分隔）', () => {
    expect(parseCharacter('bremerton_(scorching-hot_training)_(azur_lane)')).toEqual({
      name: 'bremerton', costumes: ['scorching-hot_training'], work: 'azur_lane',
    });
  });

  // 特殊字元:撇號
  it('作品/角色名含撇號（不可當分隔）', () => {
    expect(parseCharacter("hk416_(girls'_frontline)")).toEqual({
      name: 'hk416', costumes: [], work: "girls'_frontline",
    });
    expect(parseCharacter("jeanne_d'arc_alter_(avenger)_(fate)")).toEqual({
      name: "jeanne_d'arc_alter", costumes: ['avenger'], work: 'fate',
    });
    expect(parseCharacter("ninomae_ina'nis")).toEqual({
      name: "ninomae_ina'nis", costumes: [], work: null,
    });
  });

  // 無括號:只角色名,無作品
  it('無括號角色名', () => {
    expect(parseCharacter('hatsune_miku')).toEqual({ name: 'hatsune_miku', costumes: [], work: null });
    expect(parseCharacter('cirno')).toEqual({ name: 'cirno', costumes: [], work: null });
  });

  // qualifier 黑名單:單尾端括號為限定詞 → 歸 costume,不歸作品
  it('單括號限定詞歸造型不歸作品', () => {
    expect(parseCharacter('fujimaru_ritsuka_(male)')).toEqual({
      name: 'fujimaru_ritsuka', costumes: ['male'], work: null,
    });
    expect(parseCharacter('joseph_joestar_(young)')).toEqual({
      name: 'joseph_joestar', costumes: ['young'], work: null,
    });
    expect(parseCharacter('konpaku_youmu_(ghost)')).toEqual({
      name: 'konpaku_youmu', costumes: ['ghost'], work: null,
    });
  });

  // qualifier 前還有括號群組 → 前者才判 work(spec §測試考量 line 188）
  it('限定詞前的群組才判作品', () => {
    expect(parseCharacter('xxx_(young)_(some_work)')).toEqual({
      name: 'xxx', costumes: ['young'], work: 'some_work',
    });
  });

  // 畸形:剝完角色名為空 → null
  it('畸形（角色名為空）回 null', () => {
    expect(parseCharacter('_(foo)')).toBeNull();
  });
});

describe('displayOf', () => {
  // ② 對照表命中:有 emoji
  it('對照表命中（有 emoji,group 覆寫 kind）', () => {
    const d = displayOf({ name: ':3', kind: 'general', source: 'wd14', confidence: 0.5 });
    expect(d.label).toBe('貓嘴');
    expect(d.emoji).toBe('😺');
    expect(d.group).toBe('expression'); // 覆寫 general
    expect(d.canonical).toBe(':3'); // canonical 不變
  });

  // ② 對照表命中:emoji 空字串 → null(找不到貼切時保留 canonical)
  it('對照表命中但無貼切 emoji → emoji null', () => {
    const d = displayOf({ name: 'mouth_hold', kind: 'general' });
    expect(d.label).toBe('嘴叼');
    expect(d.emoji).toBeNull();
    expect(d.group).toBe('expression');
  });

  // ④ character 命中對照表外 → 解析括號(輸出已 spaces)
  it('character 解析:角色_(作品)', () => {
    const d = displayOf({ name: 'aris_(blue_archive)', kind: 'character' });
    expect(d.group).toBe('character');
    expect(d.label).toBe('aris');
    expect(d.costumes).toEqual([]);
    expect(d.work).toBe('blue archive');
    expect(d.canonical).toBe('aris_(blue_archive)');
  });

  it('character 解析:角色_(造型)_(作品)', () => {
    const d = displayOf({ name: 'asuna_(bunny)_(blue_archive)', kind: 'character' });
    expect(d.label).toBe('asuna');
    expect(d.costumes).toEqual(['bunny']);
    expect(d.work).toBe('blue archive');
  });

  it('character 解析:冒號作品名（spaces 後保留冒號）', () => {
    const d = displayOf({ name: 'trailblazer_(honkai:_star_rail)', kind: 'character' });
    expect(d.label).toBe('trailblazer');
    expect(d.work).toBe('honkai: star rail');
  });

  it('character 解析:限定詞歸造型、無作品徽章', () => {
    const d = displayOf({ name: 'fujimaru_ritsuka_(male)', kind: 'character' });
    expect(d.label).toBe('fujimaru ritsuka');
    expect(d.costumes).toEqual(['male']);
    expect(d.work).toBeNull();
  });

  // 非 character kind 不解析括號(① 底線轉空白,無作品徽章)
  it('general 的括號不誤判為作品', () => {
    const d = displayOf({ name: 'star_(symbol)', kind: 'general' });
    expect(d.group).toBe('general');
    expect(d.label).toBe('star (symbol)');
    expect(d.work).toBeUndefined();
  });

  it('general 含作品名但歸 general 時不誤判', () => {
    const d = displayOf({ name: 'vision_(genshin_impact)', kind: 'general' });
    expect(d.group).toBe('general');
    expect(d.label).toBe('vision (genshin impact)');
    expect(d.work).toBeUndefined();
  });

  // ① 退回:對照表未命中、非 character → 底線轉空白
  it('未命中 → 底線轉空白退回', () => {
    const d = displayOf({ name: 'long_hair', kind: 'general' });
    expect(d.label).toBe('long hair');
    expect(d.emoji).toBeNull();
    expect(d.group).toBe('general');
    expect(d.canonical).toBe('long_hair');
  });

  // 畸形 character → parseCharacter null → 退回 ①
  it('畸形 character 退回底線轉空白', () => {
    const d = displayOf({ name: '_(foo)', kind: 'character' });
    expect(d.group).toBe('character');
    expect(d.label).toBe(' (foo)');
    expect(d.work).toBeUndefined();
  });

  // 來源 / confidence 透傳
  it('透傳 source 與 confidence', () => {
    const d = displayOf({ name: '1girl', kind: 'general', source: 'wd14', confidence: 0.92 });
    expect(d.source).toBe('wd14');
    expect(d.confidence).toBe(0.92);
  });
});

describe('groupTags', () => {
  it('依固定序分組 character/copyright/expression/general/meta', () => {
    const lanes = groupTags([
      { id: 1, name: 'long_hair', kind: 'general' },
      { id: 2, name: 'aris_(blue_archive)', kind: 'character' },
      { id: 3, name: ':3', kind: 'general' },
    ]);
    expect(lanes.map((l) => l.group)).toEqual(['character', 'expression', 'general']);
  });

  it('expression 從 general 拉出自成一區', () => {
    const lanes = groupTags([
      { id: 1, name: 'blush', kind: 'general' },
      { id: 2, name: '1girl', kind: 'general' },
    ]);
    expect(lanes.find((l) => l.group === 'expression')?.tags.map((t) => t.canonical)).toEqual(['blush']);
    expect(lanes.find((l) => l.group === 'general')?.tags.map((t) => t.canonical)).toEqual(['1girl']);
  });

  it('非顯示 group(manual/path）不丟,附在已知 group 之後', () => {
    const lanes = groupTags([
      { id: 1, name: 'my_fav', kind: 'manual', source: 'manual' },
      { id: 2, name: 'long_hair', kind: 'general', source: 'wd14' },
    ]);
    const groups = lanes.map((l) => l.group);
    expect(groups).toContain('general');
    expect(groups).toContain('manual');
    expect(groups.indexOf('general')).toBeLessThan(groups.indexOf('manual'));
  });

  it('帶 id 與 character 造型/作品徽章', () => {
    const lanes = groupTags([{ id: 7, name: 'asuna_(bunny)_(blue_archive)', kind: 'character' }]);
    const t = lanes[0].tags[0];
    expect(t.id).toBe(7);
    expect(t.work).toBe('blue archive');
    expect(t.costumes).toEqual(['bunny']);
  });
});

describe('對照表常數', () => {
  it('EXPRESSION_DISPLAY_MAP 條目皆為 expression group', () => {
    for (const v of Object.values(EXPRESSION_DISPLAY_MAP)) {
      expect(v.group).toBe('expression');
    }
  });
  it('NON_WORK_SUFFIX 含已驗證限定詞、且不含 swimsuit/rider', () => {
    expect(NON_WORK_SUFFIX.has('male')).toBe(true);
    expect(NON_WORK_SUFFIX.has('young')).toBe(true);
    expect(NON_WORK_SUFFIX.has('ghost')).toBe(true);
    expect(NON_WORK_SUFFIX.has('swimsuit')).toBe(false);
    expect(NON_WORK_SUFFIX.has('rider')).toBe(false);
  });
});
