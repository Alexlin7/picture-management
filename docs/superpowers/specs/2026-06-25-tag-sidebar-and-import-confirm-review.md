# 左側 tag 側欄 UX + 匯入確認定位 — 檢視文件

- 日期:2026-06-25
- 狀態:**檢視/選項(等使用者拍板)** —— 非設計定稿。讀完決定方向後,各自再走 `brainstorming → spec → plan`。
- 範圍:① gallery 左側 tag facet 側欄的 UX 優化;② 釐清「匯入確認(import-confirm)」的原始定位 vs 現狀,確認還有沒有用。
- 方法:ui-ux-pro-max(UX 規則庫)+ 競品研究(Danbooru / Hydrus)+ 現有實作盤點。
- 關聯:`2026-06-21-picture-management-design.md`(§5.4 路徑→tag、鐵則 9)、`2026-06-24-frontend-design-guidelines.md`(樣式落點)、`2026-06-22-remaining-work-handoff.md`(backlog:真窗格化已列)。

---

## 一、左側 tag 側欄(facet sidebar)

### 1.1 現況盤點(`src/Pm.Web/src/app/features/gallery/facet-sidebar/`)

側欄(`facet-sidebar.html`)其實是 **3 個分區**,成熟度差很多:

| 分區 | template 位置 | 現況 | 問題 |
|---|---|---|---|
| **作品/企劃 → 角色(DAG 樹)** | `facet-sidebar.html:5–88` | ✅ 可收折(`▶` toggle,3 層 depth)。`facet-sidebar.ts` 的 `defaultOpen` 把**第一層所有有子節點的節點預設展開** | 預設全展 → 一打開就很長 |
| **屬性(general)** | `facet-sidebar.html:90–103` | ❌ **平鋪、無上限、不可收**;`@for (row of general())` 把每個 general tag 全列出 | 「整排散出來」主因 |
| **年份(meta)** | `facet-sidebar.html:105–118` | ❌ 同上,平鋪 | 同上 |
| **三個分區的標題列(`.facet-t`)** | 各區頂 | ❌ 整段不可收折 | 想「收起整個屬性區」做不到 |
| 整體 | — | ❌ 無虛擬捲動、無側欄內篩選框、無 top-N 截斷 | 十萬量級時 general tag 可能上千列,全進 DOM |

**收合狀態管理(`facet-sidebar.ts:26–44`)**:`overrides: Map<FacetNode, boolean>` 記使用者手動展開/收合,`defaultOpen` 是預設。**僅 DAG 樹節點適用**;general/meta 與分區層級沒有收合概念。狀態**不持久化**(重整即回預設)。

> 結論:使用者說的「整排 tag 散出來」= **屬性/年份兩區平鋪 + 分區整段不可收 + 無上限**。DAG 樹本身已有收折,問題不在它。

### 1.2 競品怎麼做(研究)

- **Danbooru**:tag 依**分類分區**(Artist / Copyright / Character / General / Meta),**色碼** + 每區帶**數量**;長清單**截斷只顯示前幾個**,要看全部才展開。
- **Hydrus**:namespace 前綴 + **色碼**讓大清單裡好認重點 tag;**可收折 parent/sibling**;大清單**可在清單內即時搜尋/過濾**。
- **共通模式**:`分類分區(可收折)` + `數量` + `每區 top-N + 顯示更多` + `一個過濾 tag 的搜尋框` + `色碼`。本專案已有色碼與數量與分類分區,**缺的是:整段收折、top-N 截斷、側欄內過濾、虛擬捲動**。

來源:見文末。

### 1.3 ui-ux-pro-max 命中的規則

- **progressive-disclosure** —— 漸進揭露,別一次把所有選項倒出來(直接對應痛點)。
- **virtualize-lists** —— 50+ 項清單應虛擬化(十萬量級必中)。
- **state-transition** —— 收合/展開要 150–300ms 動畫,別硬跳。
- **Search / Autocomplete + No-Results** —— 側欄過濾框 + 查無結果提示。
- **nav active-state** —— 已選(已加入搜尋)的 tag 要在側欄高亮。
- **truncation-strategy** —— 長 tag 名截斷 + tooltip,別撐爆側欄寬度。

### 1.4 建議方向(依優先序;實作前各自再開 spec/plan)

1. **三個分區整段可收折**
   - `.facet-t` 標題列加 `▸/▾` chevron,點擊收合整區;狀態存 localStorage(重整保留)。
   - 直接滿足「收折」訴求,改動小、風險低。**建議第一刀。**

2. **屬性/年份 改 top-N + 「顯示更多 N 個」**
   - 預設依 count 只顯示前 ~10–15 個,其餘摺疊;點「顯示更多」才全展。
   - 解決「平鋪散出」的核心。

3. **側欄頂部「篩選標籤…」inline 過濾框**
   - 打字即時縮小側欄清單(純前端過濾現有 facet 資料);查無 → 友善提示。
   - 十萬量級的剛性需求(Danbooru/Hydrus 都有)。

4. **長清單虛擬捲動**
   - `@angular/cdk/scrolling`;與 backlog「圖牆真窗格化」可共用一套窗格化思路。
   - 注意:DAG 樹是巢狀不定高,虛擬化較麻煩;可先只對 general/meta 平面清單做。

5. **DAG 樹預設收合(或記住狀態)**
   - 目前 `defaultOpen` 預設全展第一層 → 一開就很長。改成預設收合、或把 `overrides` 持久化。

6. **視覺微調**
   - 每區標題帶該區 tag 數;已選 tag 高亮(active-state);chevron 旋轉動畫(150–300ms,respect reduced-motion)。

**提案後的側欄長相(示意):**
```
篩選   12,431 命中
┌─────────────────────────┐
│ 🔎 篩選標籤…             │
└─────────────────────────┘
▾ 作品 → 角色      (●DAG)
   ▸ 蔚藍檔案          1,204
   ▸ 原神               980
▾ 屬性
   long_hair          3,201
   smile              2,876
   dress              1,540
   …顯示更多 142 個
▸ 年份                      ← 整段收起(記住狀態)
```

### 1.5 切片建議(若決定動)

- 切片 1:分區整段收折 + localStorage 狀態(滿足主訴求,最小)。
- 切片 2:屬性/年份 top-N + 顯示更多。
- 切片 3:側欄內過濾框。
- 切片 4:虛擬捲動(可併入 backlog 的真窗格化一起做)。
- 每片獨立可玩、可測、可 commit。

---

## 二、匯入確認(import-confirm)—— 定位釐清

### 2.1 原始定位(設計白紙黑字)

`2026-06-21-picture-management-design.md` **§5.4 路徑 → tag 確認(學習型)** + 鐵則 9:

> 匯入掃描收集所有出現過的路徑段 → 比對 `path_tag_rule`:已有規則的段直接套用(map_to_tag / ignore / meta_year);**沒見過的新段**列入確認清單給使用者決定動作 → 寫回 `path_tag_rule`。鐵則 9:「路徑→tag 是『匯入後確認』,確認結果存 `path_tag_rule`(每段只確認一次)。不要改成全自動硬塞。」

白話:**把你的資料夾結構,一次性吸收成 tag**。資料夾段 = 你既有的人工分類;確認一次(map 選分類 / ignore 不產標 / 標為年份),之後該路徑下的圖自動帶上對應 tag(`source=path`),重掃只問新段。

### 2.2 現狀(實作對照)

`src/Pm.Web/src/app/features/manage/import-confirm/` + `manage.store.ts`:

- 流程**仍正常接線運作**:`loadImport → api.pendingSegments(rootId) → 每列確認 → applyRule×N → applyPathTags`。
- UI:一張表(路徑段 / 出現次數 / 範例路徑 / 動作),動作三選一(map+分類 / ignore / year),底部「套用全部並完成匯入」「略過全部」。
- 與設計**一致**,沒有壞掉。

### 2.3 跟你記憶中的「個人預設 tag」對不對得上?

**基本對齊**:它就是「用你的資料夾當預設分類來源,一次確認變成可搜 tag」。所以「個人預設 tag」這個印象沒錯 —— 只是它的預設**來自資料夾路徑**,不是你憑空列的清單。

### 2.4 跟原意的漂移 / 現有限制(值得知道)

1. **只取第一個 root**:`manage.store.loadImport()` 用 `this._roots()[0]`,**沒有 root 選擇器**(當初標 deferred)。多個圖庫來源時,只會問第一個來源的路徑段 —— 這是最實際的缺口。
2. **資料夾段驅動,非自由預設**:它從**既有資料夾名**長出 tag,不是「你自己定義一組常用 tag」。
3. **一次性 confirm 流**:是「匯入時確認」的流程,不是長駐的「預設 tag 管理頁」。

### 2.5 需要你定調的分岔(決定是「修補」還是「新做」)

你心目中的「個人預設 tag」比較接近哪個?

- **(A) 資料夾 → tag(= 現在的 import-confirm)**
  → 功能健在,定位正確。要改善的是**補多 root 選擇器**(讓多來源都能確認),其餘維持。屬「修補現有」。

- **(B) 自訂常用 tag、選圖時一鍵套用(preset / quick-apply)**
  → 這是**全新功能**,跟 import-confirm 無關:讓你預先定義一組常用 tag(或 tag 組合),在圖牆/檢視器選圖後一鍵套上(`source=manual`)。要另開 brainstorming → spec。

- **(C) 兩者都要**:A 留著(資料夾→tag),另做 B(快速套標)。

> 知道是 A / B / C,才知道是「修補 import-confirm」還是「設計新功能」。

---

## 三、總結(睡醒只要回這三件)

1. **側欄要不要動?** 若要,從「分區整段收折(切片 1)」開始最划算。
2. **import-confirm 的『個人預設 tag』是 A / B / C?** 決定修補 or 新做。
3. 任何一項確定要做,我再走 `brainstorming → spec → plan → 實作`。

---

## Sources(競品研究)

- [Booru tags — Grokipedia](https://grokipedia.com/page/Booru_tags)
- [We deserve better boorus in 2026 — Latte's Blog](https://blog.lattemacchiato.dev/we-deserve-better-boorus-in-2026/)
- [Hydrus — Getting started: Tags](https://hydrusnetwork.github.io/hydrus/getting_started_tags.html)
- [Hydrus — More Tags](https://hydrusnetwork.github.io/hydrus/getting_started_more_tags.html)
