# WD14 tag 顯示層清理(v1)— 設計文件

- 日期:2026-06-22
- 狀態:設計定稿,待實作
- 關聯:`CLAUDE.md` 鐵則 #3(SQLite 是 tag 唯一真相)、#5(tag 來源要分);
  上層設計 `2026-06-21-picture-management-design.md`

## 背景與問題

WD14 自動標籤已在真實圖庫實機驗證(200 張、AMD/DirectML)。標籤「品質」沒問題,
但**觀感髒**,使用者實際遇到三類:

1. **命名難讀** — danbooru 底線格式 `long_hair`;角色名內嵌作品名 `aris_(blue_archive)`。
2. **看不懂的符號** — `:o` `:t` `:3` `:d` `^_^` 等。這些是 **danbooru 的「表情顏文字」標籤**
   (WD14 沿用 danbooru 標籤體系),不是壞資料,只是命名不直覺。
3. **來源混雜難分** — `path` / `manual` / `wd14` 三種來源的 tag 混在同一條清單,
   分不出哪些是親手策展、哪些是機器猜的。

關鍵事實:WD14 v3 的 `selected_tags.csv` 只有 category `0`(general)/`4`(character)/
`9`(rating,被過濾),**沒有** copyright,也**沒有** expression。所有顏文字都以
category 0 = general 進來 —— 因此「表情」這個分類,必須由我們自己定義,模型不提供。

## 核心原則

**所有「變乾淨」只發生在前端顯示層,SQLite 存的 wd14 原始 tag 完全不動。**

- canonical tag 名(如 `:3`、`long_hair`、`aris_(blue_archive)`)是唯一真相,
  **照存、照搜**;顯示用的 label / emoji / 分組只是裝飾與重新編排。
- 可逆、零風險、隨時可調。對齊鐵則 #3 / #5。
- 對照表查不到的 tag → 優雅退回(套基底轉換顯示原始名),不報錯、不留空。

## 範圍(v1)

### 做這四件事

#### ① 通用基底:底線轉空白(套到所有 tag)
- 顯示時將 canonical 名的底線 `_` 轉為空白:`long_hair` → `long hair`。
- 副作用(正面):角色名 `aris_(blue_archive)` → `aris (blue archive)`,
  自動成為 pixiv-style「名字 (作品)」形式,即使不解析也已好讀(④ 再進一步把結構拆成徽章)。
- 純前端字串處理,canonical 照存照搜。

#### ② tag 顯示對照表(第一版只建「表情組」)
- 一張前端靜態對照表:`canonical → { label, emoji?, group? }`。
- 第一版只填**顏文字 + 常見表情單字**(約 30–50 條),例:

  | canonical | label | emoji | group |
  |---|---|---|---|
  | `:3` | 貓嘴 | 😺 | expression |
  | `:o` | 張嘴 | 😮 | expression |
  | `:t` | 嘟嘴 | 😤 | expression |
  | `:d` | 張嘴笑 | 😃 | expression |
  | `^_^` | 瞇眼笑 | 😄 | expression |
  | `;)` | 眨眼 | 😉 | expression |
  | `:p` | 吐舌 | 😝 | expression |
  | `blush` | 臉紅 | 😊 | expression |
  | `crying` | 哭 | 😢 | expression |
  | `closed_eyes` | 閉眼 | 😌 | expression |

  (完整清單於實作時定;不求全,覆蓋常見即可。)
- 顯示形式:`emoji + label + canonical`,例 `😺 貓嘴 :3`。canonical 永遠可見可搜。
- 帶 `group: "expression"` 的 tag,在檢視器中**從 general 拉出來,自成「表情」一區**。

#### ③ 檢視器依 kind 分組 + 來源徽章
- tag 不再混成一條清單,依 kind 分區顯示:
  **character / copyright / 表情(expression)/ general / meta**。
- 每個 tag 標來源徽章:`wd14 87%`(帶 confidence 百分比)/ `manual` / `path`。
- 空的 kind 區不顯示。
- **窄側欄版面(2026-06-24 實作補,RWD 條件折疊):** 檢視器是窄側欄,長標(尤其 character 帶 ‹造型›/‹作品› + 長 canonical + 來源)單行會橫向爆寬。做法**不是**「依 kind 硬分單/雙行」,而是**內容驅動的 RWD**:每個 tag 都是同一種 chip(`display:flex; flex-wrap:wrap`),內含兩群組 —— `tmain`(emoji+label+‹造型›/‹作品›)與 `tmeta`(canonical(僅當 ≠ label)+ 來源% + 移除鈕)。**放得下就同一行;放不下時 `tmeta` 整段自動折到第二行**(灰、小)。短標(`1girl` 等)維持單行、且可多欄並排;只有過長者才折。**canonical 仍永遠可見可搜**(降為副行、未隱藏,守原則)。純 CSS、無 JS 量測;`displayOf`/`groupTags` 邏輯不動。

#### ④ character 標括號的 kind-aware 顯示解析
danbooru 角色標用「限定詞(qualifier)」括號慣例。實機資料的模式:

| 模式 | 例子(真實) | 拆解 |
|---|---|---|
| 角色_(作品) | `aris_(blue_archive)` | 角色 + 作品 |
| 角色_(造型)_(作品) | `aris_(maid)_(blue_archive)`、`ako_(dress)_(blue_archive)` | 角色 + 造型 + 作品 |
| 角色名含底線 | `doodle_sensei_(blue_archive)` | 多詞角色名 + 作品 |
| 作品名含特殊字元 | `trailblazer_(honkai:_star_rail)` | 角色 + 作品(內含 `:` `_`) |

**關鍵不變式:括號意義取決於 kind。** 在 `character` 標上,**最後一組括號=作品**,
其前的括號=造型/變體;在 `general` 標上,括號只是消歧義、**不是作品**
(如 `star_(symbol)`、`diamond_(shape)`;另有 `vision_(genshin_impact)` 雖含作品名卻被歸 general)。

**規則(顯示層、純前端、canonical 不動):**
- **只對 `kind === "character"` 套用**;其餘 kind 一律維持 ①(底線轉空白),不解析括號。
- 從 canonical 名**尾端**反覆剝離 `_(<不含括號內容>)` 群組:
  - **最後一組** → 作品(copyright),以徽章 `‹作品: blue archive›` 顯示。
  - **中間各組** → 造型/變體,以徽章 `‹造型: maid›` 顯示(可多個)。
  - 剩下的前段 → 角色名,套底線轉空白當主 label。
- **防禦性退回**:character 標若尾端無括號群組 → 只顯示角色名、無徽章;
  完全不符模式者退回 ①。手動塞的非慣例標因此不會被誤拆。
- 各段顯示一律套底線轉空白;canonical(`aris_(maid)_(blue_archive)`)永遠是搜尋鍵。

**qualifier 例外(2026-06-23 對抗式驗證補:單括號歧義)**

「最後一組括號=作品」對**單一尾端括號**有根本歧義:`fujimaru_ritsuka_(male)`、
`joseph_joestar_(young)`、`konpaku_youmu_(ghost)` 的 `(male)`/`(young)`/`(ghost)`
是 danbooru 的**限定詞(qualifier)**,不是作品 —— 照原規則會渲染 ‹作品: male›
這種誤導徽章(正是本案要消滅的「觀感髒」)。

- 維護一份小型 **`NON_WORK_SUFFIX` 黑名單**(純前端常數,約 10–20 條:
  `male` / `female` / `young` / `old` / `aged_up` / `child` / `ghost` / `cosplay` / `alternate` …,實作時定)。
- **剝離出的「最後一組」若命中黑名單**:不歸 work,改歸 **costume**;
  其前若還有括號群組則照常往前剝(那組才可能是 work)。
- 黑名單只在判定 work/costume 歸屬時查,**不影響 canonical、不影響搜尋**;命中與否都不報錯。

**實作不變式(對抗式驗證釘死,實作層務必守):**
- 剝離 regex 必須是 `/_\(([^()]*)\)$/` **反覆套用**,直到尾端無括號群組。
  **嚴禁**把冒號 `:`(`honkai:_star_rail`、`re:zero`)、斜線 `/`(`fate/extra`)、
  連字號 `-`(`scorching-hot`)、撇號 `'`(`jeanne_d'arc`、`girls'_frontline`)當分隔或排除字元
  —— 這四種字元各補一條單元測試。
- `parseCharacter` **完全信任 `kind === "character"`**;kind 正確性由資料層 / WD14 來源保證。
  畸形輸入(剝完角色名為空字串,如 `_(foo)`)→ 回 `null` 退回 ① 底線轉空白防呆。

### v1 明確不做(記為未來)
- 雜訊摺疊(`1girl` / `solo` / `looking_at_viewer` 等高頻低鑑別度標收起)。
- general 通用標的中文譯名(對照表的可選擴充,清單龐大且持續成長)。
- 資料層清理:黑名單 / 別名 / 門檻調整(會動到「存什麼」,需另案明示)。
- **資料層**作品/造型軸:真的 parse 出 `blue_archive` / `maid` 寫成獨立 tag,
  以支援布林篩「所有 blue archive」或同角色不同造型歸併。④ 只做**顯示層**呈現,
  不產生可查詢的資料維度;此資料層補軸與 DAG 階層同屬大題,deferred。
- tag DAG 階層(查父標命中子標)—— 後端 `TagClosureService` 基建已有,當之後大題。
- WD14 佇列數 / 總命中數 / 採用拒絕 UI / 失敗 job 重試(原「WD14 體驗補完」其餘項,另案)。

## 顯示決策演算法

給定一個 tag(canonical `name`、`kind`、`source`、`confidence?`),決定它怎麼顯示:

```
displayOf(tag):
  base = { canonical: tag.name, source: tag.source, confidence: tag.confidence }

  entry = displayMap[tag.name]              # ② 查對照表(以 canonical 為鍵)
  if entry exists:
     return { ...base, label: entry.label, emoji: entry.emoji ?? null,
              group: entry.group ?? tag.kind }   # group 覆寫 kind 決定分區

  if tag.kind == "character":               # ④ character 括號 kind-aware 解析
     parsed = parseCharacter(tag.name)      # → { name, costumes[], work? } 或 null
     if parsed != null:
        return { ...base, group: "character",
                 label:    spaces(parsed.name),
                 costumes: parsed.costumes.map(spaces),   # 徽章 ‹造型: …›
                 work:     parsed.work ? spaces(parsed.work) : null }  # 徽章 ‹作品: …›

  return { ...base, label: spaces(tag.name), emoji: null, group: tag.kind }  # ① 退回:底線轉空白

# spaces(s) = s.replaceAll('_', ' ')
# parseCharacter:用 /_\(([^()]*)\)$/ 反覆剝離尾端括號群組(順序保留)。
#   最後一組:若命中 NON_WORK_SUFFIX 黑名單 → 當 costume(往前看下一組才可能是 work);
#            否則 → work。中間各組一律 costumes;剩餘前段=name。
#   無尾端括號群組 → { name, costumes: [], work: null };剝完 name 為空 → 回 null(退回 ①)。
```

- 分區依 `group`(對照表可覆寫,否則用 `kind`)。
- 搜尋 / 加標籤 / 既有 tag 比對一律用 `canonical`,顯示層不影響資料與查詢。

## 元件落點(實作參考,非綁定)

- **顯示對照表 + `displayOf` 純函式**:放前端 core(如 `core/tags/tag-display.ts`),
  純函式無副作用、可單元測試、gallery 與 inspector 共用。
- **檢視器分組**:`features/inspector` 模板改為依 `group` 分區 + 來源徽章;
  既有 combobox 加 / 刪標籤邏輯(走 canonical)不動。
- 後端**不需改動**(photo detail 已回 `tags[].source` 與 `confidence`)。

## 不變式 / 對齊鐵則

- 不修改、不寫回原圖(本案純前端,天然滿足)。
- SQLite 仍是 tag 唯一真相;顯示層不寫任何資料。
- 來源分明(徽章顯式呈現 path / manual / wd14)。

## 測試考量

- `displayOf` 純函式單元測試:
  - 對照表命中(有 emoji / 無 emoji / 有 group 覆寫)。
  - 未命中 → 底線轉空白退回。
  - canonical 不因顯示而改變(搜尋鍵不變)。
- `parseCharacter`(④)單元測試(只對 kind=character):
  - `aris_(blue_archive)` → name `aris`、無造型、作品 `blue archive`。
  - `aris_(maid)_(blue_archive)` → name `aris`、造型 `[maid]`、作品 `blue archive`。
  - `ako_(dress)_(blue_archive)` → name `ako`、造型 `[dress]`、作品 `blue archive`。
  - `doodle_sensei_(blue_archive)` → name `doodle sensei`、作品 `blue archive`。
  - `trailblazer_(honkai:_star_rail)` → name `trailblazer`、作品 `honkai: star rail`。
  - 無括號的 character 名 → 只角色名、無徽章(防禦退回)。
  - **非 character kind 不解析**:`star_(symbol)`(general)→ 走 ①`star (symbol)`,不出作品徽章;
    `vision_(genshin_impact)`(general)同理不誤判為作品。
  - **qualifier 黑名單**(對抗式驗證補):`fujimaru_ritsuka_(male)` → name `fujimaru ritsuka`、
    `male` 歸**造型**不歸作品、無作品徽章;`joseph_joestar_(young)`、`konpaku_youmu_(ghost)` 同理。
    黑名單 qualifier 前若還有括號群組,前者才判 work(`xxx_(young)_(some_work)` → work=`some work`、造型 `[young]`)。
  - **特殊字元不可當分隔**(各一條):冒號 `re:zero` / `nier:automata`、斜線 `tamamo_no_mae_(fate/extra)`、
    連字號 `bremerton_(scorching-hot_training)_(azur_lane)`、撇號 `hk416_(girls'_frontline)` →
    作品/造型內含該字元但仍正確剝為單一群組。
  - **畸形防呆**:`_(foo)`(剝完 name 為空)→ 回 `null` 退回 ①。
- 檢視器:多 kind 混合 tag → 正確分區;空 kind 區不顯示;character 區出造型/作品徽章;
  來源徽章與 confidence 正確。

## 開放問題(實作時定)

- 表情對照表的具體條目與 emoji 對應(覆蓋常見即可,可逐步補)。
- label 語言:表情組用繁中(`貓嘴`)較易讀;general 基底維持原文(底線轉空白)。
- emoji 跨平台長相不一 —— 定位為輔助圖示,不取代 canonical 文字。
