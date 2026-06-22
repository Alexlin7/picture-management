# 標籤一條龍(B+C)—— 收尾:檢視器 combobox(2026-06-22)

> **狀態(2026-06-22 已完成):** 檢視器 combobox 已實作(`InspectorStore.suggest/clearSuggestions`、
> `inspector.ts` 的 `onType/move/onEnter/pick/createNew/close`、`inspector.html` 浮層、`inspector.css`)。
> `ng build` 通過、整合啟動 0 console error。**尚待你做的真實資料手動驗證見 §4**。
> B+C「標籤一條龍」三塊(後端去重 / 管理頁 / 檢視器 combobox)至此全部完成。

承接「B+C 標籤一條龍」。後端 + 管理頁已交付,**唯一剩下的是檢視器加標籤的 combobox**。
本文件說明:現況、要做什麼、設計取捨、驗證方式。動工前先看這份。

---

## 1. 現況(已交付,已 commit)

| 項目 | 內容 | commit |
|---|---|---|
| 後端標籤庫端點 | `GET /api/tags?q=&limit=`、`PUT/DELETE /api/tags/{id}`、`POST /api/tags/{id}/merge/{targetId}`;`TagService` 正規化(trim + 摺疊空白,**不強制小寫**)+ 不分大小寫 upsert 去重;13 測試 | `f838476` |
| 標籤庫管理頁 `/tags` | 列表 + 分色 + 使用數;過濾框;改名(撞既有名→後端自動合併);刪除(confirm,級聯解除圖上關聯) | `7d44588` |

**正規化決策(已定案,勿改回小寫底線):** 比對用不分大小寫(`Name.ToLower()`),但**保留顯示拼寫**(`VSpo!`、角色名大小寫不被吃掉)。撞名一律合併,不另開合併 UI。

---

## 2. 要做什麼 —— 檢視器加標籤改 combobox(修 #2)

**現況問題:** `inspector.html` 67–77 行是純文字輸入框,打字 + Enter 直接建標籤。
缺點:看不到既有標籤、容易打出近似重複(大小寫/多空白),不知道庫裡有沒有。

**目標互動:**

1. 打字 → debounce 查 `GET /api/tags?q=` → 下方浮出**既有相符標籤清單**(分色點 + 名 + 使用數)。
2. 點清單某項 / ↑↓ + Enter → 用**那個既有標籤名**加到這張圖(走既有 `addTag`,後端 upsert 命中既有,不會建新)。
3. **只有「沒有完全相符(不分大小寫)」時**,清單底部才顯示「＋ 建立新標籤『xxx』」一列;點它才真的建新。
4. Esc 收起清單;選完清空輸入框。

**不做:** 多選 chips、kind 下拉(維持 manual);分頁(limit 預設夠用)。保持小而可驗。

---

## 3. 實作切面(預計)

- **`InspectorStore`**:加 `suggest(q)` → 呼 `api.tags(q, 8)`,寫入 `suggestions` signal;`clearSuggestions()`。
  (或在元件本地管 signal + 直接注入 `PmApi`;傾向放 store,與既有 `addTag/removeTag` 同層。)
- **`inspector.ts`**:`onType(v)`(debounce 經 timer / `toSignal`+`debounceTime` 擇一,傾向簡單 setTimeout)、
  `pick(row)`、`createNew(name)`、鍵盤 ↑↓/Enter/Esc;`exactMatch` computed(不分大小寫比對 suggestions)。
- **`inspector.html`**:67–77 行換成 input + 浮層清單(`@for suggestions` + 條件式「建立新標籤」列)。
- **`inspector.css`**:浮層定位(input 下方,絕對定位)、hover/active 列樣式、分色點沿用 `TAG_COLOR`。
- `PmApi.tags(q, limit)` 已存在(管理頁用同一支),**不需動後端**。

---

## 4. 驗證(完成定義)

1. `ng build` 過。
2. 整合 app(單一 .NET 程序 serve)實測:
   - 打「海」→ 浮出「海綿寶寶」既有列(帶使用數);點它 → 圖上多一個既有標籤,**庫裡標籤數不變**(沒建新)。
   - 打一個全新名 → 沒相符 → 出「建立新標籤」列;點它 → 建新 + 掛圖 + 進標籤庫。
   - 打既有名的大小寫/多空白變體 → **命中既有、不建近似重複**(驗正規化)。
   - Esc 收清單;選完清空輸入。
   - 0 console error。
3. 通過後 commit:`feat(web): 檢視器加標籤改 combobox(查既有/防近似重複)(B+C #2)`。

---

## 5. 收尾後狀態

B+C「標籤一條龍」三塊(後端去重 / 管理頁 / 檢視器 combobox)全部完成。
**之後的選項(未排,待你決定):** D 自動入庫(FileSystemWatcher + 排程掃描)、WD14 worker Task 4–5
(host wiring + scanner gate)、`hash,tag` CSV 匯出/匯入。`feat/wd14-worker` 雜燴分支待合回 main。
