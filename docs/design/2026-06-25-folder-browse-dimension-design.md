---
status: active
last-reviewed: 2026-06-29
supersedes: []
superseded-by: []
related: []
---

# 資料夾路徑維度檢視(② 瀏覽)— 設計

- 日期:2026-06-25
- 狀態:**已實作(2026-06-29 複查確認)** —— `/browse`(即時樹 + 麵包屑 + 下鑽 + 遞迴圖牆)已落地;原設計經 brainstorming 收斂 + 可點 mockup 審定。
- 範圍:新增「照資料夾分類瀏覽」維度,與既有「by tag 搜尋」並列,成為圖庫的第二個檢視入口。
- 方法:brainstorming(一問一答收斂)+ 資料層實地探索 + ui-ux-pro-max 規則 + frontend-design 視覺判斷 + 可點 mockup 三輪迭代。
- Mockup:`docs/mockups/folder-dimension-design.html`(瀏覽器開,3 分頁:雙維度總覽 / 資料夾瀏覽完整互動 / path→tag 對照)。
- 關聯:`2026-06-21-picture-management-design.md`(§5.4 路徑→tag、鐵則 1/2/4/9)、左側 tag facet 側欄 UX 檢討(本設計回應其中「資料夾維度」分岔)、`2026-06-24-frontend-design-guidelines.md`(樣式落點)。

---

## 一、問題與動機

圖庫有兩個天然的檢視維度,但目前只有第一個:

1. **by tag**(現有):布林搜尋 + facet 側欄,跨資料夾用標籤找圖。
2. **by 資料夾路徑**(缺):照使用者原本的硬碟資料夾分類逐層瀏覽。

現況下「資料夾」在匯入時經 path→tag(§5.4)被**攤平成 tag**(`source=path`):一條 `2024/蔚藍檔案/foo.png` 切段後產生 tag「2024」(kind=meta)、「蔚藍檔案」(kind=path/character),貼標靠 `RelPath LIKE '%/蔚藍檔案/%'`。這帶來兩個資訊損失:

- **階層脈絡丟失**:tag「蔚藍檔案」脫離了「它在 Pixiv/2024 下」這件事。
- **同名段合併**:不同父夾下的同名資料夾(Pixiv 下的「蔚藍檔案」、Twitter 下的「蔚藍檔案」)合併成同一顆 tag,無法區分。
- 另外它**依賴匯入確認**(每段確認一次),沒確認過的段不成 tag。

這對「跨夾布林查詢」是好的(這正是搜尋維度要的),但對「我記得放在哪個夾、想照結構逛」沒有幫助。使用者明確希望補上後者,且兩種情境(純 tag 找 / 照資料夾找)都會用到 → **兩個功能、兩個入口**。

## 二、核心決策

| # | 決策 | 理由 |
|---|---|---|
| D1 | 「瀏覽」是**獨立入口**(activity bar + 獨立路由 `/browse`),與 `/gallery` 並列、狀態互不干擾 | 已是 folder 領域,概念與搜尋分開更乾淨 |
| D2 | 資料夾樹**直接讀 `photo_location.rel_path` 即時建**,不落表、不改 schema | `rel_path` 已存完整相對路徑且正規化為 `/`;反映硬碟**當下**結構,免匯入確認、不依賴 path tag |
| D3 | 圖牆**遞迴**顯示:點一個夾 = 看它**含子夾**的全部圖,計數同此 | 圖庫看圖情境要「這夾連同子夾都攤給我看」 |
| D4 | 側欄資料夾樹只展 **1–2 層**;更深的夾靠**主區「子資料夾」可點晶片**往下鑽 | 遞迴下深層圖在上層已可見,側欄不必展到底→不爆長;深層進入移到主區 |
| D5 | 夾內疊 tag 用**扁平自動完成清單**(非樹),且**只列該夾範圍內實際存在**的 tag + 各自張數 | 夾內 tag 通常不多,扁平最快;只列範圍內存在者→避免選了就 0 結果 |
| D6 | path tag **維持不動**,繼續服務搜尋維度;兩維度並存、餵同一份 `photo_location` | 兩者正交、各取所長,不衝突 |
| D7 | 範圍計數語意 = 該路徑前綴下的 **distinct present photo 數** | 與既有 facet/search 的 `status='present'` 過濾一致;一張多 location 不重複算 |

**不違反鐵則**:純讀取、不碰原圖、不寫 XMP(鐵則 1);身分仍是 hash、位置仍走 `photo_location`(鐵則 2);軟刪 `archived` 的 location 不進樹(鐵則 4,靠 `status='present'`)。

## 三、資料層(現成,無需改 schema)

事實(來自程式碼探索):
- `PhotoLocation`:`PhotoId` / `LibraryRootId` / `RelPath`(≤1024,正斜線正規化,如 `"Pixiv/2024/foo.png"`)/ `Status`(`present`/`missing`/`archived`)。`(library_root_id, rel_path)` 唯一索引。
- `LibraryRoot`:`Id` / `Name` / `AbsPath`。一個圖庫可有多個 root。
- `PhotoQueryService.SearchAsync(all, none, afterId, pageSize)`:include tag → 後代閉包(遞迴 CTE)→ AND 多群組、排除 none;`p.Locations.Any(l => l.Status=="present")` 為基底。
- `TagFacetService`:示範了「拉 present location/tag 進記憶體聚合」的既有作法。

→ 「真實資料夾樹 + 範圍過濾」全部可由 `rel_path` 推導,與既有查詢正交。

## 四、API 設計

### 4.1 資料夾樹

```
GET /api/roots/{rootId}/folder-tree
→ FolderNode {
    name: string,          // 該層資料夾名(根節點為 root.Name)
    relPath: string,       // 累積相對路徑前綴,如 "Pixiv/2024"(根為 "")
    photoCount: number,    // 該前綴下 distinct present photo 數(遞迴)
    children: FolderNode[] // 直接子資料夾
  }
```

- 多 root:`GET /api/folder-roots` 回所有 root 摘要(id/name/photoCount)供頂層選擇;進入某 root 後取其樹。
- 實作:拉該 root 全部 present location 的 `(RelPath, PhotoId)`,記憶體建樹;後序計算 `photoCount`(用 `HashSet<long>` 去重再取 count,或子集合併)。淺樹一次回完整結構;前端只展 1–2 層顯示。
- 效能:十萬量級為字串/整數聚合,單次掃描;若日後嫌重再加快取或 SQL 前綴 GROUP BY。

### 4.2 範圍內查詢(遞迴 + 夾內疊 tag)

複用搜尋管線,`SearchDto` 增 `rootId?` 與 `pathPrefix?`:

```
POST /api/search        (+ rootId, pathPrefix)
POST /api/search/count  (+ rootId, pathPrefix)
```

- `pathPrefix` 過濾:`p.Locations.Any(l => l.Status=="present" && l.LibraryRootId==rootId && (pathPrefix=="" || l.RelPath.StartsWith(pathPrefix + "/")))`。
  - 用 `pathPrefix + "/"` 比對避免 `Pixiv` 誤中 `Pixiv2`;根層 `pathPrefix==""` 即整 root。
- `all`/`none` tag 條件照舊(夾內疊 tag = `all` 帶若干 tag)→ 天然就是「資料夾範圍 AND tag」。
- 圖牆沿用既有 keyset 無限捲(`afterId`/`pageSize`)。

### 4.3 夾內可用 tag(自動完成)

```
GET /api/browse/folder-tags?rootId=&path=
→ [{ name, kind, count }]   // 該前綴範圍內 distinct photo 的 tag 聚合,count desc
```

- 與 `TagFacetService` 同路數,但加 `pathPrefix` scope;供「+tag」自動完成,只列範圍內存在的 tag 與張數。
- 前端再按使用者輸入做 substring 過濾(同 gallery topbar 既有作法)。

## 五、前端設計

- 新 route `/browse`,新 feature 目錄 `features/browse/`;activity bar 增「資料夾」入口(沿用既有 SVG icon 風格,folder 色 `--t-meta` 系)。
- **BrowseStore**(`providedIn: 'root'`,與 GalleryStore 平行):
  - signal:`folderTree`、`currentRootId`、`currentPath`、`breadcrumb`(由 path 推導)、`subfolders`(currentPath 的直接子)、`photos`(遞迴+tag 篩,無限捲累積)、`hitCount`、`innerTags`(SearchToken[],夾內疊的 tag)、`availableTags`(自動完成來源)。
  - 動作:`enterFolder(relPath)`、`addInnerTag` / `removeInnerTag`、`loadMore`。狀態進 URL query(`?root=&path=&q=`,可重整/分享,沿用 gallery 的 URL 單一真相模式)。
- 元件(每個單一職責、可獨立測):
  1. `folder-tree-sidebar`:淺樹(1–2 層),節點 `app-folder-row`(icon+名+遞迴數),點擊 `enterFolder`,active 高亮(`nav-state-active`)。
  2. `browse-breadcrumb`:路徑麵包屑(`breadcrumb-web`),點任一層回上層。
  3. `subfolder-bar`:當前夾直接子夾的可點晶片(深層下鑽);無子夾則不顯示。
  4. `inner-tag-filter`:夾內疊 tag 帶 = 既有 chip + combobox 自動完成(可考慮與 gallery topbar 的 combobox 抽共用,deferred)。
  5. 圖牆:複用 `<app-thumb>` + 無限捲(IntersectionObserver),不重造。
- **空狀態**(`empty-states`,frontend-design「空畫面是邀請」):
  - 夾內無圖:「這個資料夾沒有圖片。」+(若有子夾)提示點子夾。
  - 疊 tag 後 0 結果:「此資料夾內沒有符合的圖片」+ 一鍵清除夾內 tag。
- a11y/motion:沿用全域 `:focus-visible` ring 與 `prefers-reduced-motion`;chevron/晶片 hover 150ms;不蓋 focus ring。樣式落點遵守 `2026-06-24-ui-style-system-design.md`(component .css 只手寫 + `var(--token)`)。

## 六、實作切片(每片 build+test 綠後 commit)

1. **後端樹**:`FolderTreeService` + `GET /folder-tree` + `GET /folder-roots`;測試(建樹/遞迴去重計數/archived 不入樹/同名子夾不合併)。
2. **後端範圍查詢**:`SearchDto` 加 `rootId`/`pathPrefix`,`PhotoQueryService` 套前綴過濾;`GET /browse/folder-tags`;測試(前綴邊界 `Pixiv` 不中 `Pixiv2`、根層=整 root、範圍 AND tag、夾內 tag 聚合)。
3. **前端骨架**:`/browse` 入口 + `BrowseStore` + 樹側欄 + 麵包屑 + 子夾下鑽 + 遞迴圖牆(接 1+2);URL 狀態。
4. **前端夾內疊 tag**:`inner-tag-filter` 自動完成 + chip;清除回純瀏覽。
5. **空狀態 / 邊界打磨**:空狀態文案、深層下鑽、多 root 頂層、reduced-motion 檢查。

## 七、固定決策(實作時不要回頭推翻)

- 即時樹讀 `rel_path`,**不落表、不改 schema**;path tag 不動。
- **多 root(確定)**:進 `/browse` 頂層**並排列出所有 root 為第一層節點**(各帶 photoCount);點某 root 展其樹、麵包屑以該 root 名起頭。`BrowseStore.currentRootId` 隨之切換,範圍查詢一律帶 `rootId`。
- 遞迴顯示與計數(含子夾);計數 = distinct present photo。
- 側欄樹淺(1–2 層),深層靠主區子夾晶片下鑽。
- 夾內 tag 扁平、只列範圍內存在者(非完整 tag 樹)。
- 視覺沿用既有深色工作台識別,不另起爐灶;設計重心在互動結構。
- 瀏覽為獨立入口/路由,與搜尋狀態隔離。

## 八、開放項 / deferred

- 夾內 tag 若扁平清單不夠(夾內 tag 暴多)再考慮分組/樹(D5 已留伏筆)。
- 圖牆真窗格化(virtual scroll)沿用 backlog,本功能不另做。
- 「在資料夾樹節點上右鍵 → 把此夾設為 path tag 規則」之類的 tag/folder 互通,deferred。
- 排序(名稱/數量/時間)、每夾縮圖預覽,nice-to-have。
- **「在檔案總管開啟原檔位置」(deferred,可選加值)**:圖牆/inspector 加按鈕,後端用 `LibraryRoot.AbsPath + RelPath` 即時組絕對路徑、呼叫 `explorer.exe /select,"path"`(開總管選中檔,唯讀、不執行、不解析 PNG 內容→不踩鐵則 1)。**資安鎖法:只接 `photoId`/`locationId`,後端反查 DB 自組路徑,絕不接受前端傳路徑字串(防 path injection);僅桌面模式提供,headless/NAS 不開。** 圖牆是縮圖(`<app-thumb>`,依 hash 快取),原圖一律不直接拉。此功能性質偏 inspector 通用(搜尋維度亦可用),非資料夾維度核心,故 deferred。
