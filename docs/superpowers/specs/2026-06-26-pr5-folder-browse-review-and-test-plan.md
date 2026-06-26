# PR #5(資料夾瀏覽維度)合併後 review 結果 + 測試計畫

> 產出日期:2026-06-26
> 範圍:PR #5 `feat/folder-browse-dimension` merge 進 `main` 的 code diff(後端 `FolderTreeService` / `PhotoQueryService` 範圍過濾、Angular `/browse` 前端、Serilog log 級別重構)。
> 用途:把 `/code-review high` 的 findings 落成可追蹤的修復項 + 各層測試案例(TDD / e2e / 瀏覽器),供逐項補測與修復。

---

## 0. 測試基礎建設現況(寫測試前必讀)

| 層 | 工具 | 指令 | 現況 |
|---|---|---|---|
| 後端單元/整合(TDD) | xUnit + EF Core + SQLite(每測試獨立 `.sqlite` 檔) | `dotnet test` | ✅ 已就緒(`tests/Pm.Api.Tests`、`tests/Pm.Scanner.Tests`) |
| 後端 API e2e | `WebApplicationFactory<Program>`(in-proc 起真實 pipeline) | `dotnet test` | ✅ 已就緒(見 `FolderBrowseApiTests.cs`) |
| 前端單元(TDD) | **vitest** + jsdom(經 `@angular/build:unit-test`) | `npm test`(= `ng test`) | ✅ 已就緒(`*.spec.ts`) |
| 前端 e2e / 瀏覽器自動化 | **無** | — | ❌ **尚無 Playwright/Cypress**。需新增 infra 才能自動化(見 §3) |

**結論**:correctness findings 大多可用**現有 TDD infra**(vitest 控制 Promise 解析順序、xUnit 種資料)直接補紅燈測試;真正的「瀏覽器層」目前只能**手動 checklist**,要自動化需先導入 Playwright(`superpowers:webapp-testing` skill 已提供 Playwright 能力)。

---

## 1. Findings 總表(2026-06-26 已全部驗證 + 修復)

驗證方式:每條先寫**會失敗的測試重現**(RED),確認是真 bug 才修(GREEN);cleanup 類先放安全網測試再重構。最終全測:**後端 145 綠 + 前端 81 綠 + 瀏覽器 e2e 全過**。

| # | 檔案 | 類型 | 嚴重度 | 驗證判定 | 驗證/測試方式 | 狀態 |
|---|---|---|---|---|---|---|
| F1 | `browse.store.ts` `loadMore()` | Correctness(race) | 高 | **真** | TDD vitest(deferred)+ 瀏覽器 e2e | ✅ 已修 |
| F2 | `browse.store.ts` `search()`/`loadTree()` | Correctness(race) | 高 | **真** | TDD vitest(deferred) | ✅ 已修 |
| F3 | `Program.cs` Serilog `ParseLevel` fallback | Correctness(行為反向) | 中 | **真** | TDD xUnit(抽 `LogLevels.Parse`) | ✅ 已修 |
| F4 | `FolderTreeService.cs` `meta[c.TagId]` | Correctness(潛在 500) | 中 | **真**(需 FK-off 孤兒) | TDD xUnit(raw SQL 種孤兒) | ✅ 已修 |
| F5 | `inner-tag-filter.ts` `ensureLoaded()` | Correctness(失敗永久空白) | 中 | **真** | TDD vitest(失敗→重試) | ✅ 已修 |
| F6 | `browse-grid.ts` IntersectionObserver | Correctness(自動分頁停擺) | 低-中 | **真** | 瀏覽器 e2e(捲動補頁 60→120) | ✅ 已修 |
| F7 | `browse-grid.css`/`inner-tag-filter.css` 裸 hex | Convention(CLAUDE.md 鐵則) | 低 | **真**(且 gallery 同款) | grep 靜態檢查 + 瀏覽器截圖 | ✅ 已修(token 化) |
| F8 | `browse.store.ts` token 編解碼重抄 | Reuse | 低 | **真** | TDD characterization | ✅ 已修(用 `@core/tag-search`) |
| F9 | `PhotoQueryService.cs` predicate 重複 | Simplification/altitude | 低 | **真**(無 bug,僅漂移風險) | TDD count==page guard | ✅ 已修(抽 `ApplyFolderScope`) |
| F10 | `inner-tag-filter.ts` `rgba()` 第 4 份副本 | Reuse | 低 | **真** | TDD `hexToRgba` + NaN guard | ✅ 已修(抽 `@core/tag-color`) |

> 已查證**非 bug**(不列入):rel_path 分隔符(掃描時 `LibraryScanner.cs:57` 已 `.Replace('\\','/')` 正規化);`SearchAsync`/`CountAsync` 新增 optional 參數未破壞任何 caller(build 期即可擋)。

### 修復摘要(可追)

| # | 修法 | 新增測試 |
|---|---|---|
| F1/F2 | `BrowseStore` 加 `gen` 世代序號:`applyUrl` 每次 ++,`search`/`loadTree`/`loadMore` 寫入前比對自身 gen 是否仍為當前世代,陳舊回應丟棄 | `browse.store.spec.ts`(2) |
| F3 | 抽 `Pm.Api.LogLevels.Parse(string?) → LogEventLevel?`:`None`→Fatal(靜默)、無法解析→`null`(per-category override 跳過,不再靜默放寬為 Information) | `LogLevelsTests.cs`(10 含 Theory) |
| F4 | `FolderTagsAsync` 改 `Where(meta.ContainsKey(...))` 略過懸空 photo_tag | `FolderTreeTests`(+1) |
| F5 | `ensureLoaded` 失敗時清 `loadedKey` 讓同夾下次重試 | `inner-tag-filter.spec.ts`(2) |
| F6 | IntersectionObserver 載完一頁後 `unobserve+observe` 重評估相交,短結果集續補不停擺 | 瀏覽器 e2e |
| F7 | 新增 token `--color-hair-strong`/`--color-bar-1`/`--color-bar-2`,browse + gallery 同款 hex 一併 token 化 | grep 檢查 |
| F8 | 移除 `encodeInner`/`decodeInner`/`splitInner`,改用 `@core/tag-search` 的 `encodeTokens`/`decodeTokens`/`splitTokens`(`splitTokens` 抽共用,gallery 同步沿用) | `tag-search.spec.ts`(+1) |
| F9 | 抽 `ApplyFolderScope(IQueryable<Photo>, rootId, pathPrefix)`,`SearchAsync`/`CountAsync` 共用 | `FolderScopeQueryTests`(+1 count==page) |
| F10 | 抽 `@core/tag-color` 的 `hexToRgba`(含非 hex NaN 防呆),`inner-tag-filter` 改用 | `tag-color.spec.ts`(2) |

### 瀏覽器 e2e(新增 infra)

- 已 `npm i -D @playwright/test` + `npx playwright install chromium`。
- `src/Pm.Web/e2e/browse-smoke.mjs`:`dotnet run` 起真實 app serve 前端,Playwright `page.route` 在瀏覽器層 mock `/api`(空 DB 也能渲染),縮圖回傳彩色 SVG。
- 跑法:先起 app(`dotnet run --project src/Pm.Api --no-launch-profile --urls http://localhost:5180`),再 `cd src/Pm.Web && npm run e2e`。
- 驗證項:F6 捲動自動補頁 `60 → 120`;F1 `Pixiv/2024` 與 `Twitter` 兩夾圖無交叉;截圖 4 張存 `e2e/shots/`。

---

## 2. 各 finding 測試案例(TDD / e2e / 瀏覽器)

### F1 — `loadMore()` 跨資料夾競態:舊頁 append 進新夾、舊 cursor 覆蓋

**根因**:`loadMore`/`search` 無 generation/序號 guard,last-write-wins。
**建議修法**:store 內加遞增 `_reqSeq`;每次 `search()`/`loadMore()` 開頭擷取當下 seq,回應落地前比對 `seq === _reqSeq` 才寫入(或切夾時 `_reqSeq++` 使在途回應失效)。

#### TDD(vitest)— `browse.store.spec.ts`(新增)
- **測試名**:`loadMore_的舊資料夾回應_不得污染新資料夾的photos與cursor`
- **Given**:mock `PmApi`,`search`/`searchCount` 回傳「可手動解析」的 deferred(用 `let resolveA!: (v)=>void; new Promise(r=>resolveA=r)`)。先 `applyUrl(rootA,'')` 載入 A 的首頁,觸發 `loadMore()`(A 的下一頁請求在途、未解析)。
- **When**:`applyUrl(rootB,'')`(B 的 search 先解析完成),**之後**才解析 A 的 `loadMore` 回應。
- **Then**:`store.photos()` 只含 B 的項目;`store.hasMore()`/下一個 cursor 來自 B,不含 A 的 item、不被 A 的 cursor 覆蓋。
- **紅燈**:目前實作會把 A 的 items `[...cur, ...A]` 接上、`_nextCursor` 變 A 的游標 → 斷言失敗。

#### e2e(API 級,選配)
- 此競態純前端時序,API e2e 無法重現;**不適用**。可改在 §3 的全棧 Playwright 覆蓋。

#### 瀏覽器測試(手動 checklist)
1. 起 app,進 `/browse`,選一個圖很多的 root。
2. DevTools → Network 設 throttling「Slow 3G」。
3. 進資料夾 A,捲到底觸發 loadMore,**在 spinner 還在轉時**立刻點側欄資料夾 B。
4. **預期**:圖牆只顯示 B 的圖、計數一致;**不得**出現 A 的縮圖混入或計數對不上。
- (可選 Playwright)`page.route('**/api/search', ...)` 注入延遲,腳本化上述時序並斷言 DOM 中無 A 的 photoId。

---

### F2 — `search()` / `loadTree()` 慢回覆蓋:陳舊資料夾回應蓋掉當前

**根因**:同 F1(無序號 guard),但發生在初始 `search()` 與 `loadTree()`。
**建議修法**:同 F1 的 `_reqSeq`;`loadTree` 另以 `rootId === _rootId()` 二次確認後才 `_tree.set`。

#### TDD(vitest)— `browse.store.spec.ts`
- **測試 a**:`search_慢回的舊夾回應_不得覆蓋當前夾的photos與hitCount`
  - Given:deferred mock,`applyUrl(rootA)` 後立刻 `applyUrl(rootB)`;讓 B 先解析、A 後解析。
  - Then:`photos()`/`hitCount()` 為 B 的值。
- **測試 b**:`loadTree_舊root的樹_不得在切到新root後落地`
  - Given:`folderTree(A)` 比 `folderTree(B)` 慢回。
  - Then:`tree()`/`breadcrumb()`/`subfolders()` 對應 B,不是 A。

#### e2e / 瀏覽器
- 同 F1:手動快速切 root(Slow 3G),驗證麵包屑/側欄/圖牆三者 root 一致;Playwright 可注入 per-root 延遲腳本化。

---

### F3 — Serilog `ParseLevel` fallback 反向:`None`/空/typo → `Information`(調高冗長度)

**根因**:`ParseLevel` 的 `_ => Information`(`Program.cs:468`)把任何非列舉值映到 Information;per-category override 迴圈套用後等於把該類別「拉高」。
**建議修法**:(a) 對無法解析的值**保留呼叫者語意**(回 `null` 則跳過該 override,不硬塞 Information);(b) 支援 MS 慣例 `"None"` → 對應 Serilog 不輸出(可用 `LogEventLevel` 最高 + 額外過濾,或顯式處理)。

#### TDD(xUnit)— `tests/Pm.Api.Tests/LogLevelParsingTests.cs`(新增)
> 前置:`ParseLevel` 目前是 `Program.cs` 的 static local function,**無法直接測**。先做小重構:抽成 `internal static class LogLevels { public static LogEventLevel Parse(string?) ... }`,讓測試可 reach(同時也讓 F 系列「抽共用」一致)。
- `Parse_合法等級_對映正確`:Theory 蓋 `Trace/Debug/Information/Warning/Error/Critical` → 對應 Serilog 等級。
- `Parse_None_不得被當成Information`:`Parse("None")` 期望 ≥ `Warning`(或回 null 代表「不 override」)。**紅燈**:現況回 Information。
- `Parse_空字串與未知值_不得靜默放寬為Information`:`Parse("")`、`Parse("Warnin")` 期望回 null/拋出/最保守等級,而非 Information。

#### e2e(選配,整合)
- `WebApplicationFactory` 注入 `Logging:LogLevel:Microsoft.EntityFrameworkCore = "None"`,掛一個記憶體 Serilog sink,送一個會產生 EF Information log 的請求,斷言 sink **未**收到該類別 Information 事件。

#### 瀏覽器測試
- **不適用**(純後端 logging)。

---

### F4 — `FolderTagsAsync` 裸索引 `meta[c.TagId]`:懸空 photo_tag → `KeyNotFoundException`(500)

**根因**:`return counts.Select(c => new FolderTag(meta[c.TagId].Name, ...))` 用裸索引;同檔 `BuildRootsAsync` 對等風險刻意用 `GetValueOrDefault`。
**建議修法**:`meta.TryGetValue(c.TagId, out var m)`,缺列則跳過(或記 Warning)。

#### TDD(xUnit)— `tests/Pm.Scanner.Tests/FolderTreeTests.cs`(新增 case)
- **測試名**:`FolderTags_遇懸空photo_tag_不丟例外而是略過該tag`
- **Given**:種 1 張 present 圖 + 1 筆指向**不存在 tagId** 的 `photo_tag`。
  - 注意:FK cascade 開著時正常無法插孤兒。測試需在**同一條連線**先 `PRAGMA foreign_keys=OFF` 後用 raw SQL `INSERT INTO photo_tag(...)` 插入懸空列(模擬歷史 FK-off 遺留資料)。
- **When**:`FolderTagsAsync(rootId, "")`。
- **Then**:不丟例外;回傳的清單**不含**該懸空 tagId(其餘正常 tag 正確)。
- **紅燈**:現況 `meta[c.TagId]` 丟 `KeyNotFoundException`。

#### e2e(API 級)— `FolderBrowseApiTests.cs`(新增 case)
- **測試名**:`FolderTags_有懸空標籤時_端點仍回200`
- 種同上孤兒資料 → `GET /api/browse/folder-tags?rootId=..&path=..` 斷言 `200`(現況會 500)。

#### 瀏覽器測試
- 手動:僅在 DB 已有歷史孤兒列時可見;一般環境難自然觸發 → 以上 TDD/e2e 為主,瀏覽器**選配**(若有舊 DB,進該夾開 +tag 自動完成不應整頁報錯)。

---

### F5 — `ensureLoaded()` 暫時性失敗 → 該夾自動完成永久空白(不重試)

**根因**:先佔 `loadedKey` 再 await;`catch` 又把 `this.all=[]` 快取在該 key 下,頂端 `if (key === this.loadedKey) return` 使後續不再嘗試。
**建議修法**:失敗時**清掉 `loadedKey`**(或記 `loadedKey = ''`),讓下次 `onType` 重新載入;成功才定住 key。

#### TDD(vitest)— `inner-tag-filter.spec.ts`(新增)
- **測試名**:`folderTags_第一次失敗後_再次輸入應重試而非永久空白`
- **Given**:mock `PmApi.folderTags` 第 1 次 reject、第 2 次 resolve 一組 tag。
- **When**:`onType('a')`(失敗)→ 再 `onType('ab')`。
- **Then**:第 2 次有發出 API 呼叫且 `suggestions()` 非空。
- **紅燈**:現況第 2 次短路,`suggestions()` 恆空、`folderTags` 只被呼叫 1 次。
- **附帶 case**(F5 衍生 staleness):`快速連打_最終建議應依最後一次輸入term過濾`(若一併修 race 再補)。

#### e2e
- 不適用(需控制單一 API 暫時性失敗,API e2e 不易注入)→ 走 vitest。

#### 瀏覽器測試(手動)
1. 進某夾,DevTools → Network 對 `**/api/browse/folder-tags` 設 **Block request URL** 一次。
2. focus `+tag` 輸入框打字 → 應無建議(預期失敗)。
3. 解除 block,**不切夾**再打字。
4. **預期**:重新載到該夾 tag 並出現建議(現況:仍空白,須切夾才恢復)。

---

### F6 — IntersectionObserver 在結果集落在 rootMargin 內時不再 re-fire,自動分頁停擺

**根因**:IO 只在「相交狀態轉態」時觸發;sentinel 恆在 DOM、`rootMargin:'600px'`,內容一直短於 viewport+600px 就不再回呼,`hasMore()` 仍 true。
**建議修法**:`loadMore()` 完成後若仍 `hasMore()` 且 sentinel 仍相交,主動再拉一頁(loop until 不相交或無更多);或改用 `IntersectionObserver` + 載入後重新檢查 `takeRecords()`/手動量測。

#### TDD(vitest)
- **可測性差**:jsdom 不做 layout,IO 不會真正計算相交。可做**邏輯層**測試:把「載完一頁後若 sentinel 仍可見則續載」抽成純函式/store 方法 `maybeAutoLoadMore()`,測「`hasMore && sentinelVisible` → 再呼叫 loadMore;`!hasMore` → 不呼叫」。屬重構後才好測。

#### e2e / 瀏覽器(主力)— Playwright(需新增 infra,見 §3)
- **測試名**:`短結果集_自動分頁應載到底而不停擺`
- Given:mock/種一個會回傳「每頁 60、總量略大於一頁、但版面很矮」的夾。
- When:進該夾、不手動捲動。
- Then:輪詢直到 `hasMore` 為 false 或 photo 數達總量;斷言**未卡在第一/二頁**。
- 手動版:在矮版面夾(大量極小縮圖或單欄)進入後不捲動,觀察是否自動補滿;或縮小視窗高度重現。

---

### F7 — component `.css` 寫裸 hex,違反 CLAUDE.md 樣式鐵則

**違反規則(可引用)**:CLAUDE.md 前端樣式慣例規則 4 —— 「**顏色/字體/圓角/陰影/elevation 一律走 token(`styles.css` `@theme`,同時產 utility 與 `var`),不寫裸 hex**」。
**違規行**:`browse-grid.css` `.topbar` 漸層 `#181b21`/`#15171c`、`.tile:hover` `border-color:#3b4150`;`inner-tag-filter.css` `.addinput` `#3b4150`(`.tile:hover` 的 `rgba(0,0,0,0.8)` 陰影亦屬 off-token)。
**建議修法**:這些 hue 進 `styles.css` `@theme` 成 token(如 `--color-raised-2`/`--color-hair-strong`/既有 `--shadow-*`),component `.css` 改 `var(--token)`。

#### TDD(靜態檢查,可自動化)— `styles-no-bare-hex.spec.ts`(新增)或 CI grep
- **測試名**:`browse_元件css不得出現裸hex色碼`
- 讀 `features/browse/**/*.css` 內容,regex `#[0-9a-fA-F]{3,8}\b` 應**無命中**。
- 亦可做成 CI 步驟:`rg -n '#[0-9a-fA-F]{3,8}' src/Pm.Web/src/app/features/**/*.css` 非空即 fail。
- **紅燈**:現況命中上述 4 處。

#### 瀏覽器測試
- 視覺回歸:改 token 前後比對 `/browse` topbar/晶片/輸入框配色一致(手動或 Playwright 截圖比對)。

---

### F8 — `encodeInner`/`decodeInner`/`splitInner` 重抄既有共用 helper

**重複對象**:`@core/tag-search` 已 export `encodeTokens`(同 `','` join)、`decodeTokens`(同 split→general);gallery 已有 `splitTokens`(同 `'-'` 前綴邏輯)。
**建議修法**:browse 改 import `@core/tag-search`;`splitInner` 與 gallery 的 `splitTokens` 抽到 `@core/tag-search`。

#### TDD(characterization,先鎖行為再重構)— `browse-tree.spec.ts` 或新 spec
- **測試名**:`browse的token編解碼_與tag-search一致(防分歧)`
- 對一組樣本(含含空白、含 `-` 前綴、含 CJK)斷言 `encodeInner(x) === encodeTokens(x)`、`decodeInner(q)` 與 `decodeTokens(q)` 等價。
- 重構後此測試保證行為不變;若未重構,先當「兩實作必須一致」的防漂移網。

#### e2e / 瀏覽器
- 不適用(內部純函式)。重構後跑既有 `tag-search.spec.ts` + browse 測試綠燈即可。

---

### F9 — 資料夾範圍 predicate 在 `SearchAsync` 與 `CountAsync` 逐字重複

**建議修法**:抽 `IQueryable<Photo> ApplyFolderScope(this IQueryable<Photo>, long? rootId, string? pathPrefix)` 共用。
**測試重點**:防止「count 與 page 用不同 predicate 而對不上」。

#### TDD(回歸 guard,xUnit)— `FolderScopeQueryTests.cs`(新增 case)
- **測試名**:`同一範圍下_CountAsync與SearchAsync結果數一致`
- Given:種跨多層、含兄弟前綴(`Pixiv` vs `Pixiv2`)的資料。
- Then:對多組 `(rootId, prefix)`,`CountAsync(...) == SearchAsync(...).Items.Count`(在 pageSize 足夠時)。
- 此測試在抽共用 helper **前**就先放,確保重構不改行為。

#### e2e(API 級)— `FolderBrowseApiTests.cs`
- `POST /api/search/count` 的 total 與 `POST /api/search` 的 items 數,在同 `(rootId,pathPrefix)` 下一致。

#### 瀏覽器
- 手動:進某夾,toolbar「含子夾 N 張」應等於實際捲到底載入的張數(小夾驗證)。

---

### F10 — `rgba(hex,a)` 第 4 份逐字副本(假設 `tagColor` 必回 6 碼 `#rrggbb`)

**重複對象**:`import-confirm.ts`、`inspector.ts`、`photo-grid.ts` 已有同名 helper。
**建議修法**:抽到 `@core/tag-color`(或 `@core/color`)`hexToRgba(hex, a)`,四處共用;順帶加防呆(非 6 碼回退)。

#### TDD(vitest)— `tag-color.spec.ts`(新增)或抽出後的 spec
- **測試名**:`hexToRgba_正確轉換6碼hex`、`hexToRgba_非6碼輸入不產生NaN`
- Given:`'#3b82f6'` → `'rgba(59,130,246,0.12)'`;`'#abc'`/`'var(--x)'` → 不得出現 `NaN`。
- **紅燈**(防呆部分):現況 `parseInt('abc'...)`/`'var(--x)'.slice(1)` 會產生 `NaN`。

#### e2e / 瀏覽器
- 視覺:夾內疊 tag 晶片顏色正確(手動;Playwright 截圖比對選配)。

---

## 3. 若要自動化 e2e / 瀏覽器層(F1/F2/F5/F6 的真實時序)

現況**無 Playwright/Cypress**。要把上面標「手動」的時序類 bug 自動化,建議:

1. 導入 **Playwright**(`superpowers:webapp-testing` skill 已具備此能力):`npm i -D @playwright/test`,建 `src/Pm.Web/e2e/`。
2. 以 `dotnet run --project src/Pm.Api`(serve 前端靜態檔 + 真實 API)起單一程序,Playwright 連 `http://localhost:<port>`。
3. 用 `page.route(...)` 注入延遲/失敗,腳本化「切夾競態」「folder-tags 失敗重試」「短結果集自動分頁」。
4. 種子資料:測試前用 API 或直接建一個固定 seed 的測試 SQLite(沿用後端測試的 seed 模式)。

> 在 infra 到位前,F1/F2/F5/F6 的**瀏覽器層**以本文件 §2 的**手動 checklist** 驗收;**邏輯層**盡量用 vitest(deferred Promise 控制解析順序)覆蓋,這對 race 類已足夠當紅燈。

---

## 4. 指令速查

```powershell
# 後端 TDD + API e2e(整合)
dotnet test                                   # 全測試
dotnet test tests/Pm.Scanner.Tests            # 只跑 scanner(F4/F9)
dotnet test tests/Pm.Api.Tests                # 只跑 API e2e(F3/F4/F9)

# 前端 TDD(vitest)
cd src/Pm.Web ; npm test                      # 全 spec(F1/F2/F5/F8/F10)

# 靜態檢查(F7)
rg -n '#[0-9a-fA-F]{3,8}' src/Pm.Web/src/app/features/**/*.css

# 手動瀏覽器驗收(F1/F2/F5/F6)
dotnet run --project src/Pm.Api               # 起單一程序,瀏覽器開 /browse 照 §2 checklist
```

## 5. 建議修復順序

1. **先補紅燈測試**(TDD-first,符合 CLAUDE.md「計畫先行 + 小切片」):F1/F2(vitest race)、F4(xUnit 孤兒)、F3(抽 `ParseLevel` 後測)。
2. **修對應實作** → 轉綠。
3. **cleanup 切片**:F9(抽 `ApplyFolderScope`,先放 count==page guard)、F8/F10(抽共用 helper,先放 characterization)、F7(token 化 + 靜態檢查)。
4. **e2e/瀏覽器**:F5/F6 手動驗收;若導入 Playwright,補 §3 自動化。
