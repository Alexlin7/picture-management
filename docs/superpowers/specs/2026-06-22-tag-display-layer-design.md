# WD14 tag 顯示層清理(v1)— 設計文件

- 日期:2026-06-22
- 狀態:設計定稿,待實作(使用者整理圖庫資料中,資料就緒後再動工)
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

### 做這三件事

#### ① 通用基底:底線轉空白(套到所有 tag)
- 顯示時將 canonical 名的底線 `_` 轉為空白:`long_hair` → `long hair`。
- 副作用(正面):角色名 `aris_(blue_archive)` → `aris (blue archive)`,
  自動成為 pixiv-style「名字 (作品)」形式,好讀且不需拆軸。
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

### v1 明確不做(記為未來)
- 雜訊摺疊(`1girl` / `solo` / `looking_at_viewer` 等高頻低鑑別度標收起)。
- general 通用標的中文譯名(對照表的可選擴充,清單龐大且持續成長)。
- 資料層清理:黑名單 / 別名 / 門檻調整(會動到「存什麼」,需另案明示)。
- 從 `aris_(blue_archive)` 拆出 copyright 軸(合併形式已夠用,現看來非必要)。
- tag DAG 階層(查父標命中子標)—— 後端 `TagClosureService` 基建已有,當之後大題。
- WD14 佇列數 / 總命中數 / 採用拒絕 UI / 失敗 job 重試(原「WD14 體驗補完」其餘項,另案)。

## 顯示決策演算法

給定一個 tag(canonical `name`、`kind`、`source`、`confidence?`),決定它怎麼顯示:

```
displayOf(tag):
  entry = displayMap[tag.name]            # 查對照表(以 canonical 為鍵)
  if entry exists:
     label   = entry.label
     emoji   = entry.emoji ?? null
     group   = entry.group ?? tag.kind    # group 覆寫 kind 決定分區
  else:
     label   = tag.name.replaceAll('_', ' ')   # 基底:底線轉空白
     emoji   = null
     group   = tag.kind
  return { canonical: tag.name, label, emoji, group, source: tag.source, confidence: tag.confidence }
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
  - `aris_(blue_archive)` → `aris (blue archive)`。
  - canonical 不因顯示而改變(搜尋鍵不變)。
- 檢視器:多 kind 混合 tag → 正確分區;空 kind 區不顯示;來源徽章與 confidence 正確。

## 開放問題(實作時定)

- 表情對照表的具體條目與 emoji 對應(覆蓋常見即可,可逐步補)。
- label 語言:表情組用繁中(`貓嘴`)較易讀;general 基底維持原文(底線轉空白)。
- emoji 跨平台長相不一 —— 定位為輔助圖示,不取代 canonical 文字。
