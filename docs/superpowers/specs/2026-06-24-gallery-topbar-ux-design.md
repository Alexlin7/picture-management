# Gallery 頂端操作區 UX 重構(Spec 3)— 設計文件

- 日期:2026-06-24
- 狀態:**設計定稿,待 review → writing-plans → 實作**
- 關聯:`2026-06-24-ui-style-system-design.md`(Spec 1 地基已實作)、
  `2026-06-22-tag-display-layer-design.md`(顯示層 displayOf/parseCharacter)、
  memory `ui-search-scan-ux-concern`(使用者原始疑慮)
- 起因:使用者反映 gallery 頂端「搜尋列像在學語法、反人類」「掃描鈕不知道在幹嘛」。
  經 ui-ux-pro-max audit + 讀碼,確認三項問題並定本案。**走 prototype-first:
  搜尋重構先做成可玩版本,迭代再補細節**(使用者明示「先玩過才知道哪裡有問題」)。

## audit 結論(本案要解的)

1. **搜尋列**:tag-token 布林 + autocomplete。問題在「假扮搜尋卻逼打運算子語法」
   (空格=AND、`-`=排除,提示寫在 placeholder 一加 token 就消失)。但 autocomplete
   帶 kind 分色 + 張數是好的,要留。
2. **掃描鈕**(頂端 primary):語意是 per-root 檔案系統掃描,但頂端**無 root context**;
   且**圖庫來源頁 `roots.html` 早有 per-root「重新掃描」**(`onRescan(r.id)`,同 icon)。
   → 頂端這顆是**冗餘 no-op 複製品**。
3. **儲存搜尋鈕**:語意合理但目前 no-op,且無 token 時也可按。

## 鎖定決策(brainstorming 過程)

- **搜尋範圍 = 純 tag,不加檔名/全文**。理由:產品命題就是「tag 跟檔案系統脫鉤」;
  動漫圖檔名多無語意;「打字找圖」的正解是 Phase 2 CLIP 語意搜尋,非檔名 grep。
- **搜尋核心模型 = 下拉驅動 substring 探索**(使用者拍板):打片段 → 下拉列出
  canonical(含括號內作品/角色)substring 命中的既有 tag → 挑。運算子從「必須打字」
  退成「點選/隱含」。
- **掃描鈕 = 直接刪**(冗餘於 roots 頁,不在別處重建)。
- **中文顯示名反查** = 納入但僅 curated 顯示名(表情等);角色名無翻譯不受影響。

## ① 搜尋重構(本案主體,第一個可玩切片)

### 比對模型
- 打任意片段 → 下拉列「`/api/tags?q=&limit=` 回傳的既有 tag」,**帶 kind 分色 + 張數**,
  依**張數 desc**(後端既有排序)排;下拉行內顯示解析結構(例:`mika 〔blue_archive〕 角色 · 1.2k`,
  用 displayOf/parseCharacter)。
- **後端現況**:`TagService.ListAsync` 已是「`NameCi` 不分大小寫 `Contains` + 依使用數排序 + limit」。
  打「blue」本來就撈出 `blue_eyes`/`blue_archive`/`mika_(blue_archive)`。→ **核心純前端,不動後端**。

### 互動(運算子退場)
- **AND 隱含**:挑第一個 tag、再挑第二個 → token 間自動 AND。不用打空格當運算子。
- **空白正規化**:typeahead 進行中,輸入的空白在送查詢前正規化成 `_`(「blue archive」→ 查 `blue_archive`),
  **不在打字途中拆 AND**。token 只在「從下拉挑」或「精準 Enter」時形成。
- **排除改點選**:點既有 token chip 切 include/排除(排除維持現行紅色刪除線樣式),取代打 `-`。
- **精準 Enter**:輸入字 exact 命中某 canonical → 直接套(用該 tag 正確 kind);否則從下拉挑;
  皆不中 → 下拉顯示「查無此標」(不空白,符合 ux no-results 準則)。
- **常駐提示**:把運算子說明從 placeholder 改為下拉底部/旁邊**不消失**的極簡 legend。

### 中文顯示名反查
- 打 curated 顯示名(如「微笑」)→ 前端用 tag-display map 反查得到英文 tag(smile)併入下拉。
  僅覆蓋 curated 顯示名;角色名(mika)本就 canonical substring 命中,免翻譯。

### 邊界
- 高頻片段(如「blue」)命中多 → 靠後端張數排序 + limit 收斂;limit 可略放大並保留「載入更多/精煉」提示。
- 不做萬用字元/正則/typo 容錯(YAGNI;substring + 張數排序已夠玩)。

## ② 掃描鈕:刪除
- 移除 `photo-grid.html` 頂端 `.btn primary` 掃描鈕(冗餘於 roots 頁 per-root 重新掃描)。
- gallery 頂端 primary 槽空出;不在 gallery 重建掃描。若日後要「全部重掃」屬 library 維護動作,另案。

## ③ 儲存搜尋:情境化接線
- 接 `POST /api/saved-searches`(後端就緒),存當前 token 查詢。
- **無 token 時 disable**(`.btn[disabled]`,Spec 1 已有三態樣式)。
- 存完導引/連結到既有「收藏的搜尋」頁。

## ④ 批次 requeue 入口
- 後端 `POST /api/tag/requeue`(就緒)。在 gallery 提供清楚入口:作用於**當前查詢結果**或**已選取**範圍
  (沿用既有 selection)。預設置於 toolbar(命中數列那排)旁,文案明確區分「重標 ≠ 重掃」。
- 細節(作用域 UI、確認對話)留 writing-plans;本案只定「要有此入口且語意清楚」。

## 不做(YAGNI / 延後)
- 檔名/全文/語意搜尋(語意 = Phase 2 CLIP)。
- 搜尋運算子的萬用字元/正則/typo 容錯。
- gallery 端「全部重掃」、行為層設定頁(屬 scanner-refactor §D,已 deferred)。
- 中文反查擴及非 curated 角色名(無翻譯來源)。

## 驗收
- `ng build` 0 錯、`ng test` 既有測試仍綠;搜尋若抽純函式(空白正規化/反查)補 spec 測試(TDD)。
- 手測情境:打「blue」下拉出多個含 blue 的 tag 並可挑;打「blue archive」找到 `blue_archive`/`mika_(blue_archive)`;
  打「微笑」找到 smile;挑兩 tag = AND 收窄;點 chip 切排除;無 token 時儲存搜尋為 disabled;頂端無掃描鈕。
- Playwright 截圖對照頂端操作區改版前後。

## 切片順序(prototype-first)
1. **搜尋重構**(①)—— 先做成可玩,使用者實玩後再迭代。**這是第一刀,其餘暫緩等回饋**。
2. 掃描鈕刪除(②,順手、低風險)。
3. 儲存搜尋接線(③)。
4. 批次 requeue 入口(④)。

## 對齊鐵則 / 慣例
- 純前端為主(①②③核心不動後端;④只接既有端點);不碰 SQLite / 原圖 / canonical tag。
- 小切片、逐步 commit;每片 `ng build` + 起 app 手測;純函式走 `ng test`(TDD)。
- 顯示清理只動前端 display model,不改 SQLite canonical(沿用顯示層既定鐵則)。
