# WD14 顯示層 v1 — 資料準備產出 + 實作接手

- 日期:2026-06-23
- 狀態:**已實作(Slice A/B 完成,2026-06-24)**(spec 設計見 `2026-06-22-tag-display-layer-design.md`,§④ 已鎖 qualifier 黑名單)
- 來源:dynamic workflow `tag-display-v1-dataprep`(6 agents,從本機 `src/Pm.Api/models/wd14/selected_tags.csv` 撈、依全 danbooru 全球 count 排序、對抗式驗證 parseCharacter)
- 為何不依賴使用者圖庫:對照表權威來源是 WD14 完整字典 + 全球 count,比任何單機圖庫更全;本機 300 張只當事後 sanity check。

## 實作計畫(下次接手從這裡開始)

前端**有** Karma/Jasmine(`ng test` + 既有 `.spec.ts`),純函式可跑 TDD。

### Slice A — 純函式 + 對照表(TDD,可跑 `ng test`)
新增 `src/app/core/tag-display.ts`(放 core 根,與既有 `tag-color.ts` 同層):
- `EXPRESSION_DISPLAY_MAP`(下方 71 條,可直接貼)、`NON_WORK_SUFFIX`(下方草案)。
- `spaces(s) = s.replaceAll('_', ' ')`。
- `parseCharacter(name)`:反覆套 `/_\(([^()]*)\)$/` 剝尾端括號群組(順序保留);最後一組命中 `NON_WORK_SUFFIX` → 歸 costume(往前看下一組才可能 work),否則 work;中間各組 costumes;剩餘前段 name;無括號群組 → `{name, costumes:[], work:null}`;剝完 name 空 → 回 `null`。
- `displayOf(tag)`:① 查 `EXPRESSION_DISPLAY_MAP`(以 canonical 為鍵)命中→用其 label/emoji/group(group 覆寫 kind);② `kind==='character'` 且 `parseCharacter` 非 null → group `character` + costume/work 徽章;③ 退回底線轉空白。
- `tag-display.spec.ts`:涵蓋 spec §測試考量全部 + 下方 corpus。
- **驗收**:`ng test` 綠。

### Slice B — Inspector 分組 + 徽章(手測)
改 `src/app/features/inspector/inspector/{inspector.ts,inspector.html,inspector.css}`:
- lane 改依 `displayOf` 的 `group` 分區(**character / copyright / expression / general / meta**),expression 從 general 拉出;**修正現有把 `path`/`manual`(其實是 source)當 kind 塞進 `laneOrder` 的錯誤**(`TagView` 同時帶 `kind` 與 `source`)。
- 每 tag 顯示 `emoji + label + canonical`;character 區出 `‹造型:…›`/`‹作品:…›`;每 tag 加來源徽章 `wd14 NN%` / `manual` / `path`。
- combobox 加/刪邏輯(走 canonical)**不動**。
- **驗收**:`ng build` + 起 app(本機已開 `Inference:Wd14:Enabled`,launchSettings 未提交)手測 300 張;順便檢查庫裡真出現的表情標有沒有漏。

### 範圍界線
只動 inspector;gallery per-tile chips 本就 deferred,v1 不碰。不動後端/SQLite。雜訊摺疊、general 中文譯名、資料層作品軸 = spec 已列「v1 不做」。

### 對抗式驗證關鍵發現(已寫進 spec §④)
單一尾端括號歧義:`(male)`/`(young)`/`(ghost)` 是 qualifier 非作品,照原規則會渲染 ‹作品: male› 垃圾徽章 → 決議加 `NON_WORK_SUFFIX` 黑名單(命中歸造型)。實作不變式:regex 反覆套 `/_\(([^()]*)\)$/`,**嚴禁**把 `:` `/` `-` `'` 當分隔(各補一測);畸形回 null。

## NON_WORK_SUFFIX 草案(實作時可增刪)

```
male, female, young, old, aged_up, child, teenage, adult,
alternate, cosplay, ghost, human, beast, swimsuit(視情況)
```
> 注意:`swimsuit` 多數情況是真造型,慎入黑名單;`(rider)`/`(lancer)`/`(maid)`/`(bunny)` 都是正常造型,**不要**列入。

## EXPRESSION_DISPLAY_MAP(71 條,依全球 count 降序;可直接貼為 TS 草案)

> 與 spec 既有 10 條一致。`crying`→😭、`tears`→😢(刻意區隔)。emoji 空字串=找不到貼切(`mouth_hold`/`one_eye_covered`),保留。多個 canonical 共用 label(嘟嘴/邪笑/得意)無妨,canonical 永遠可見可搜。

```ts
// canonical → { label(繁中), emoji(可空), group }
export const EXPRESSION_DISPLAY_MAP: Record<string, { label: string; emoji: string; group: 'expression' }> = {
  'blush': { label: '臉紅', emoji: '😊', group: 'expression' },
  'smile': { label: '微笑', emoji: '🙂', group: 'expression' },
  'open_mouth': { label: '張嘴', emoji: '😮', group: 'expression' },
  'closed_mouth': { label: '閉嘴', emoji: '😐', group: 'expression' },
  'closed_eyes': { label: '閉眼', emoji: '😌', group: 'expression' },
  ':d': { label: '張嘴笑', emoji: '😃', group: 'expression' },
  'sweat': { label: '流汗', emoji: '😅', group: 'expression' },
  'parted_lips': { label: '微張唇', emoji: '😶', group: 'expression' },
  'teeth': { label: '露齒', emoji: '😬', group: 'expression' },
  'one_eye_closed': { label: '閉單眼', emoji: '😉', group: 'expression' },
  'tongue': { label: '露舌', emoji: '👅', group: 'expression' },
  'fang': { label: '虎牙', emoji: '🦷', group: 'expression' },
  'tongue_out': { label: '吐舌', emoji: '😛', group: 'expression' },
  'tears': { label: '眼淚', emoji: '😢', group: 'expression' },
  'grin': { label: '咧嘴笑', emoji: '😁', group: 'expression' },
  'sweatdrop': { label: '汗滴', emoji: '💦', group: 'expression' },
  ':o': { label: '張嘴', emoji: '😮', group: 'expression' },
  'lips': { label: '嘴唇', emoji: '👄', group: 'expression' },
  'saliva': { label: '口水', emoji: '💧', group: 'expression' },
  ':3': { label: '貓嘴', emoji: '😺', group: 'expression' },
  'nose_blush': { label: '鼻頭臉紅', emoji: '😳', group: 'expression' },
  '^_^': { label: '瞇眼笑', emoji: '😄', group: 'expression' },
  'expressionless': { label: '面無表情', emoji: '😑', group: 'expression' },
  'frown': { label: '皺眉', emoji: '☹️', group: 'expression' },
  'embarrassed': { label: '尷尬', emoji: '😖', group: 'expression' },
  'blush_stickers': { label: '臉紅貼貼', emoji: '😊', group: 'expression' },
  'half-closed_eyes': { label: '半閉眼', emoji: '😪', group: 'expression' },
  'happy': { label: '開心', emoji: '😄', group: 'expression' },
  'mouth_hold': { label: '嘴叼', emoji: '', group: 'expression' },
  'wavy_mouth': { label: '波浪嘴', emoji: '😣', group: 'expression' },
  'trembling': { label: '顫抖', emoji: '😨', group: 'expression' },
  'crying': { label: '哭', emoji: '😭', group: 'expression' },
  'sharp_teeth': { label: '尖牙', emoji: '🦷', group: 'expression' },
  'light_smile': { label: '淺笑', emoji: '🙂', group: 'expression' },
  ';d': { label: '眨眼張嘴笑', emoji: '😆', group: 'expression' },
  '>_<': { label: '用力閉眼', emoji: '😆', group: 'expression' },
  'clenched_teeth': { label: '咬牙', emoji: '😬', group: 'expression' },
  'drooling': { label: '流口水', emoji: '🤤', group: 'expression' },
  'surprised': { label: '驚訝', emoji: '😲', group: 'expression' },
  'anger_vein': { label: '青筋', emoji: '💢', group: 'expression' },
  'angry': { label: '生氣', emoji: '😠', group: 'expression' },
  ':<': { label: '嘟嘴', emoji: '😟', group: 'expression' },
  'tearing_up': { label: '泛淚', emoji: '🥹', group: 'expression' },
  ':p': { label: '吐舌', emoji: '😝', group: 'expression' },
  'one_eye_covered': { label: '單眼被遮', emoji: '', group: 'expression' },
  'crying_with_eyes_open': { label: '睜眼哭', emoji: '😢', group: 'expression' },
  '@_@': { label: '暈眩眼', emoji: '😵', group: 'expression' },
  ':q': { label: '舔舌', emoji: '😋', group: 'expression' },
  'naughty_face': { label: '壞笑', emoji: '😏', group: 'expression' },
  'wide-eyed': { label: '睜大眼', emoji: '😳', group: 'expression' },
  'serious': { label: '嚴肅', emoji: '😐', group: 'expression' },
  '=_=': { label: '無奈瞇眼', emoji: '😑', group: 'expression' },
  'smirk': { label: '得意斜笑', emoji: '😏', group: 'expression' },
  ':t': { label: '嘟嘴', emoji: '😤', group: 'expression' },
  ';)': { label: '眨眼', emoji: '😉', group: 'expression' },
  'pout': { label: '嘟嘴', emoji: '😗', group: 'expression' },
  'full-face_blush': { label: '滿臉通紅', emoji: '😳', group: 'expression' },
  'ahegao': { label: '阿黑顏', emoji: '😵', group: 'expression' },
  'smug': { label: '自滿', emoji: '😏', group: 'expression' },
  'wince': { label: '苦相', emoji: '😖', group: 'expression' },
  'laughing': { label: '大笑', emoji: '😆', group: 'expression' },
  '>:)': { label: '邪笑', emoji: '😈', group: 'expression' },
  'evil_smile': { label: '邪笑', emoji: '😈', group: 'expression' },
  'scared': { label: '害怕', emoji: '😱', group: 'expression' },
  'rolling_eyes': { label: '翻白眼', emoji: '🙄', group: 'expression' },
  'annoyed': { label: '煩躁', emoji: '😤', group: 'expression' },
  'sad': { label: '難過', emoji: '😞', group: 'expression' },
  'nervous': { label: '緊張', emoji: '😰', group: 'expression' },
  'sleepy': { label: '想睡', emoji: '😴', group: 'expression' },
  'shy': { label: '害羞', emoji: '☺️', group: 'expression' },
  'glaring': { label: '怒視', emoji: '😠', group: 'expression' },
};
```

下一批候選(未納入,逐步補):`teardrop`、`streaming_tears`、`nervous_sweating`、`confused`、`worried`、`disgust`、`bored`,及低頻顏文字 `:>`、`;3`、`o3o`、`x3`、`c:`、`3:`、`d:`、`x_x`、`>o<`。

## parseCharacter 測試語料(44 筆真實 tag,當 spec.ts 的 cases)

格式:`canonical → expected { name, costumes[], work }`(name/work 已底線轉空白)

- single-work:`sensei_(blue_archive)`→{sensei,[],blue archive}、`aris_(blue_archive)`→{aris,[],blue archive}、`ganyu_(genshin_impact)`→{ganyu,[],genshin impact}
- underscore-name:`hu_tao_(genshin_impact)`→{hu tao,[],genshin impact}、`doodle_sensei_(blue_archive)`→{doodle sensei,[],blue archive}、`artoria_pendragon_(fate)`→{artoria pendragon,[],fate}、`minamoto_no_raikou_(fate)`→{minamoto no raikou,[],fate}、`warrior_of_light_(ff14)`→{warrior of light,[],ff14}
- costume-work:`asuna_(bunny)_(blue_archive)`→{asuna,[bunny],blue archive}、`shiroko_(swimsuit)_(blue_archive)`→{shiroko,[swimsuit],blue archive}、`byleth_(female)_(fire_emblem)`→{byleth,[female],fire emblem}、`medusa_(rider)_(fate)`→{medusa,[rider],fate}、`jeanne_d'arc_alter_(avenger)_(fate)`→{jeanne d'arc alter,[avenger],fate}
- colon-in-work(**不可把冒號當分隔**):`trailblazer_(honkai:_star_rail)`→{trailblazer,[],honkai: star rail}、`rem_(re:zero)`→{rem,[],re:zero}、`2b_(nier:automata)`→{2b,[],nier:automata}
- 斜線/連字號/撇號(**不可當分隔**):`tamamo_no_mae_(fate/extra)`→{tamamo no mae,[],fate/extra}、`bremerton_(scorching-hot_training)_(azur_lane)`→{bremerton,[scorching-hot training],azur lane}、`hk416_(girls'_frontline)`→{hk416,[],girls' frontline}
- no-bracket:`hatsune_miku`→{hatsune miku,[],null}、`cirno`→{cirno,[],null}、`ninomae_ina'nis`→{ninomae ina'nis,[],null}
- 三括號 weird:`artoria_pendragon_(alter_swimsuit_rider)_(second_ascension)_(fate)`→{artoria pendragon,[alter swimsuit rider,second ascension],fate}、`meltryllis_(swimsuit_lancer)_(first_ascension)_(fate)`→{meltryllis,[swimsuit lancer,first ascension],fate}
- **qualifier 黑名單**(原始規則會誤判,需黑名單修正):`fujimaru_ritsuka_(male)`→{fujimaru ritsuka,**costume [male]**,work null}、`joseph_joestar_(young)`→{joseph joestar,**[young]**,null}、`konpaku_youmu_(ghost)`→{konpaku youmu,**[ghost]**,null}
- 畸形:`_(foo)`→ 回 `null`(退回 ① 底線轉空白)
- 非 character kind 不解析(在 displayOf 層測):`star_(symbol)`(general)→ `star (symbol)` 無作品徽章;`vision_(genshin_impact)`(general)→ 不誤判作品

> 完整 44 筆語料 JSON 原存於 session scratchpad(暫存會清),此處摘要已足夠建測。
