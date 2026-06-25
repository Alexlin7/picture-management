# 資料層作品軸:WD14 copyright 拆分 + tag_relation — 設計

- 日期:2026-06-25
- 狀態:**設計定稿(待 review → plan)**
- 範圍:把 WD14 角色 tag 字串裡夾的作品(`aris_(blue_archive)` 的 `blue_archive`)在**後端持久化**拆成獨立 copyright tag + 寫 `tag_relation` 邊,讓側欄 facet 樹有真實的「作品→角色」階層、且能用既有 closure 搜「整個作品」。
- 來源:這是 `2026-06-22-tag-display-layer-design.md` 第 119–122 行**明文 deferred 的「資料層作品軸 + DAG 階層」大題**。今日(2026-06-25)經盤點 + 競品驗證後正式啟動。
- 鐵則對照:守 #1(原圖唯讀)、#3(SQLite 為 tag 真相;此處**新增**衍生資料,不弱化)、#5(tag 來源分:衍生 copyright/邊標 `wd14`)。**不違反** #5 的「自動 vs 手動分」——自動拆的結果可被標籤庫手動覆寫。

---

## 一、問題(盤點實證)

1. **production `tag_relation` 是空表**:`src/` 下無任何程式寫入(只測試 fixture 手插)。故 `TagFacetService` 的 `tree` 永遠空,**所有 tag 全掉進 `rootless`**(無上層分類),綠(角色)/藍(屬性)/琥珀(年份)混成一坨 —— 使用者看到的「層級混在一起」其實是**根本沒有層級**。
2. **WD14 v3 `selected_tags.csv` 沒有 copyright(category 3)**:`blue_archive` 從不以獨立 tag 入庫,只活在 `aris_(blue_archive)` 的字串裡。`Wd14Postprocess.KindOf(3)` 程式存在但永不觸發。
3. **括號解析只在前端**:`src/Pm.Web/src/app/core/tag-display.ts` 的 `parseCharacter()`(regex `/_\(([^()]*)\)$/` 反覆剝離 + `NON_WORK_SUFFIX` 造型黑名單)**純顯示、不回寫**(顯示層 v1 刻意決策:canonical 不動)。
4. 結論:要有乾淨階層,**必須在後端 ingest 解析後綴 → 建 copyright tag + 寫 tag_relation 邊**;只給 `TagFacetService` 加 kind 過濾不夠(過濾的是空表)。

## 二、研究與決策依據(Context7 + deepwiki 交叉驗證)

| 面向 | Danbooru | Hydrus | 本專案 |
|---|---|---|---|
| 關聯儲存 | `tag_implications`(antecedent→consequent + status,人工審核) | `tag_parents`(ancestors↔descendants,人工 petition) | `tag_relation`(parent→child)+ `TagClosureService` 遞迴 CTE |
| 搜父標命中子標 | **query-time 遞迴展開**,不 materialize 到每篇 post | **virtual / query-time**,不實際掛到檔案 | `PhotoQueryService` **已用** `closure.DescendantsAsync` 展開(include + exclude) |
| 冪等/防呆 | unique 邊、防環、**防冗餘 transitive**、alias 衝突檢查 | 防環、維持 transitive closure | `TagFacetService.Build` 已有防環(`path`) |

**核心驗證**:relation-only + query-time closure(本案「甲」)= 兩大參考實作的標準做法,且**本專案搜尋側已內建**(`PhotoQueryService` 用 `DescendantsAsync`)。寫進邊後,搜 `blue_archive` 立即命中所有子標,搜尋側**零額外工**。

**唯一偏差(誠實記錄)**:Danbooru/Hydrus 的邊是**人工審核**;`_(copyright)` 後綴它們只當命名慣例、不保證是作品。本案改**自動 seed**(單人十萬張不可能手工)。緩解:造型黑名單降誤判 + 衍生邊可被標籤庫**人工覆寫**(事後 curate 等價)+ 冪等規則照搬。

## 三、設計

### 3.1 ingest 解析 helper(前端邏輯移植 C#)

新增 `src/Pm.Scanner/CopyrightAxis.cs`(或 `Pm.Ml`,實作時定;靠近 TagService 較順)——把 `tag-display.ts:parseCharacter()` 的邏輯移植成 C# 純函式:

```
ParseWork(canonicalName) -> (work: string?, isCharacterSuffix: bool)
```

- regex `/_\(([^()]*)\)$/` 反覆剝離尾端括號群組;**最右側非黑名單群組 = work**,其餘為造型(本輪丟棄,見 §五)。
- `NON_WORK_SUFFIX` 黑名單與前端**同一份語意**(造型/cosplay 等),移植時抽成共用清單常數;前端那份留著(顯示層),後端這份是 ingest 真相。**兩份需保持一致**(spec 註記,未來可考慮單一來源)。
- **canonical 不動**:`aris_(blue_archive)` 仍是 character tag 原名;本步驟只「讀出 work」。

### 3.2 自動 seed copyright tag + tag_relation 邊

只處理 **`kind == "character"` 且 `ParseWork` 解出 work** 的 tag。對每個這種 character tag `C`:

1. `var copyright = await tags.UpsertByNameAsync(work, "copyright")`（沿用既有 `TagService.UpsertByNameAsync` CI upsert + 語意升級;`work` 反底線轉空白規則沿用顯示層,canonical 名以正規化後存)。
2. upsert 邊 `tag_relation { ParentTagId = copyright.Id, ChildTagId = C.Id }`,**冪等**:
   - 已存在(parent,child)→ 不重插。
   - **防環**:若 child 已是 parent 的祖先則跳過(避免 `a→b→a`)。
   - **防自我**:parent == child 跳過。
   - **防冗餘 transitive**(採 danbooru 規則):本案階層只有 copyright→character 單層,天然無 transitive 冗餘;仍以「邊唯一」為底線。
3. 衍生邊/copyright tag 的 `source` 記 `wd14`(沿用既有 tag source 機制),供日後與人工 curate 區分。

### 3.3 觸發點

- **(a) 即時**:`TaggingWorker` 寫完 WD14 `photo_tag` 後,對本批新出現的 character tag 跑 §3.2。沿用既有 `TagService`/同 `db` scope,**同 transaction**(與既有寫入一致)。
- **(b) backfill**:一次性把現有 character tag 補上邊。走**維護端點**(與軌 2「孤兒清理」同模式):
  - `POST /api/maintenance/copyright-axis/rebuild` → 掃所有 `kind=character` tag、跑 §3.2、回 `{ copyrightsCreated, edgesCreated, scanned }`。冪等(可重跑)。
  - `.WithTags("Maintenance")`。

### 3.4 查詢 / closure(零改,確認即可)

`PhotoQueryService`(`:20`/`:27`/`:56`/`:63`)已對 include/exclude 都呼叫 `closure.DescendantsAsync(tag.Id)`。**寫進邊後,搜 `blue_archive` 自動命中 `aris_(blue_archive)` 的圖,毋須改 query。** 本 spec 不動 PhotoQueryService;實作時加一條整合測試確認此行為。

### 3.5 facet count(copyright 父節點)

`TagFacetService` 現以「直接擁有該 tag 的 present photo 數」計(刻意不展開,避免十萬量級每節點遞迴 CTE)。copyright 父節點**直接 count = 0**(無圖直接掛 copyright),顯示 0 會誤導。

**決策:copyright 節點 count = 其後代角色標的 distinct present-photo 聯集,經 closure 聚合計算 —— 成本以「copyright 節點數」為界(作品數量級,通常數十~數百),非以圖片數為界。** 角色/屬性/年份葉節點維持現行直接 count。實作擇一(實作時量測後定,spec 容許):
- (i) 對每個 copyright 節點跑一次 closure distinct 聚合(N=copyright 數,小);或
- (ii) `BuildAsync` 已載入 edges + 各 tag 直接 count,以 in-memory 近似 `父 count = Σ 直接子 count`(可能重複計到同圖,輕微高估)。

**預設取 (i) 準確版**(界在作品數,可接受);(ii) 為效能退路。

### 3.6 TagFacetService kind 分流

- `tree` / `rootless` 的頂層迴圈**加 kind 過濾**:只收 `copyright` 與 `character`。`general` / `meta` 不再進 tree/rootless(它們本就有專屬 `Top("general")`/`Top("meta")`)。
- `rootless`(無 parent 無 child 的孤立節點)過濾後只剩**無作品歸屬的 character**;前端標題改「— 角色(無作品)—」。
- 屬性(general)/ 年份(meta)區**不動**。
- 移除「general/meta tag 同時出現在 rootless 與專屬區」的重複。

### 3.7 前端 facet-sidebar(收折切片 + 收尾)

`src/Pm.Web/src/app/features/gallery/facet-sidebar/{ts,html,css}`:

- **分區整段收折**:3 區標題(作品→角色、屬性、年份)加 chevron,點擊收合整段;狀態存 `localStorage`(key `pm.facet.collapsed`,存收合中的分區集合),建構時讀回,**預設全展**。
- **rootless 改名**:「— 無上層分類 —」→「— 角色(無作品)—」。
- **年份 tooltip**:年份分區標題加 `title="你收錄／存圖的年份,非作品發行年"`(見 §四)。
- **a11y**:標題列 `role="button"` + `tabindex=0` + Enter/Space;沿用全域 `:focus-visible` ring;chevron 旋轉 respect `prefers-reduced-motion`(全域已降載)。沿用現成 `.ttoggle` 旋轉模式。
- 收合持久化邏輯抽純函式(`localStorage` 讀寫 round-trip)以 `ng test` 覆蓋;UI 改完 `ng build` + 手測。

## 四、年份語意(明確記錄,不混淆)

- meta「年份」tag 來自 **path→tag(`meta_year`)**,語意是**使用者收錄/存圖的年份**,**不是作品發行年**。
- 資料層作品軸**完全不碰年份**:copyright 軸不帶任何年;§3.1 只對 `character` kind 後綴解析,年份式後綴不會被誤建成 copyright(且 general/meta 不進此流程)。
- 前端**維持「年份」標題 + 加 tooltip**說明(§3.7),不改 kind、不改資料。

## 五、不在範圍(YAGNI / deferred)

- **造型軸**(`_(maid)` / `_(swimsuit)` 等):本輪 `ParseWork` 解出但**丟棄**,只建作品邊。造型作為可查詢維度另開。
- **作品中文顯示名 / 角色中文**:屬顯示層,與本資料層分開。
- **人工審核流程 UI**:本輪自動 seed + 標籤庫既有手動編輯即可覆寫;不另做審核佇列。
- **多層階層**(studio→series→character):WD14 只給 character+其作品,單層 copyright→character;多層等真實資料出現再說。
- **i18n / 中文基礎 tag**:衍生 copyright tag 的 canonical **維持 WD14 英文原值**(`blue_archive`),與顯示層 v1 的「EN canonical / ZH display」分工一致;中文譯名屬**顯示層**,i18n 真正啟動時再於顯示層決定(基礎 tag 英文或中文的取捨待那時定),本資料層不需改。

## 六、測試

**後端 TDD(temp SQLite,沿用 `ReconcileTests`/`PhotoMutationApiTests` 模式):**
- `ParseWork` 純函式:`aris_(blue_archive)` → `blue_archive`;`x_(blue_archive)_(maid)` → work=`blue_archive`(造型剝除);無括號 → null;純括號/空 → 安全。**與前端 `tag-display.spec.ts` 案例對齊**。
- §3.2 seed:character tag → 建 copyright tag + 邊;**冪等**(重跑不重複邊);防環/防自我。
- backfill 端點:seed 數筆 character tag(含已有/未有 work)→ 呼叫 → 邊與 copyright tag 正確、可重跑。
- closure 串接(整合):寫邊後 `GET /api/search?...`(或既有查詢端點)搜 copyright 命中子標的圖。
- `TagFacetService`:過濾後 tree/rootless 只剩 copyright+character;rootless 只剩無作品 character;general/meta 不進 tree;copyright 節點 count = 後代聯集。

**前端(`ng test` + 手測):** 收合持久化純函式 round-trip;rootless 改名 / 年份 tooltip / 收折 UI 手測。

## 七、決策日誌

- **甲(relation + query-time closure)over 乙(materialize photo_tag)**:符合 danbooru/Hydrus 標準做法 + 本專案 `PhotoQueryService` 已內建 closure;不在十萬張圖寫衍生 tag。
- **自動 seed(偏離 danbooru/Hydrus 人工審核)**:單人十萬張的務實必要;以造型黑名單 + 標籤庫人工覆寫 + 衍生來源標記緩解誤判。
- **canonical 不動,只新增 copyright tag + 邊**:守鐵則 #3/#5、延續顯示層 v1。
- **copyright count 界在作品數而非圖片數**:避免滑回「乙」的全圖寫入或十萬級每節點 CTE。
- **年份維持標題 + tooltip**:單人自知語意,最小改動;spec 留語意註記防未來誤解。
