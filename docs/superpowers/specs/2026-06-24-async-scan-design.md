# 掃描非同步化(背景跑 + 前端輪詢)— 設計文件

- 日期:2026-06-24
- 狀態:**設計定稿,待 review → writing-plans → 實作**(本次只寫設計,不動工)
- 關聯:`src/Pm.Api/Program.cs`(`POST /api/roots/{id}/scan`)、`src/Pm.Scanner/LibraryScanner.cs`(`ScanRootAsync`)、`src/Pm.Api/TaggingWorker.cs`(既有背景服務模式,可參照)、`src/Pm.Web/.../manage/roots`(前端)
- 起因:`POST /api/roots/{id}/scan` 目前**同步阻塞** —— 後端 `await scanner.ScanRootAsync(...)` 把「走訪整棵樹 + 每檔 SHA-256 + EXIF + 縮圖 + 對帳」全跑完才回應。十萬量級首次掃描可能數分鐘~數小時,整條 HTTP 請求吊著:前端按鈕卡「掃描中…」無進度;任何中間層(反向代理 `proxy_read_timeout`、瀏覽器、Kestrel)先 timeout → 504/連線斷,但後端其實還在掃 → 前後端狀態分裂。

## 決策

- **掃描改非阻塞**:`POST` 啟動背景掃描後**立刻回 `202`**,前端**輪詢**狀態端點得知進度/完成。
- **通知用輪詢,不引 SignalR/WebSocket** —— 與既有 WD14 佇列輪詢(`GET /api/tagging/stats`,gallery 4s 輪詢)一致,守住「單程序、不引 broker」取向。即時 push 與細部進度條列為後續加分。
- 把原健壯性清單 **#1(roots「重新掃描」無 `.catch`、無完成回饋)併入本案** —— 因同樣改 roots 前端,且非同步化後「卡死」根因即解。

## 設計

### 後端

**(a) 掃描狀態登記(記憶體,單程序)**
- 新增可注入 singleton `ScanStatusRegistry`,內含 `ConcurrentDictionary<long rootId, ScanStatus>`。
- `ScanStatus { string State; ScanResult? Result; string? Error; DateTimeOffset StartedAt; DateTimeOffset? FinishedAt; }`;`State ∈ running | done | error`。查無 entry = `idle`。
- **記憶體即可**:重啟時 running 狀態與掃描任務一起消失 → 狀態回 idle;重掃本來就 idempotent,無資料風險。本 spec 不做狀態持久化(YAGNI)。

**(b) `POST /api/roots/{id}/scan` 改非阻塞**
- 並發守衛:若該 root 已 `running` → **不重複啟動**,回目前狀態(避免雙擊/重入)。check-and-set 需原子(`TryAdd` 或鎖)。
- 否則:標 `running`(記 `StartedAt`)→ **啟動背景工作** → 立刻回 `202 Accepted` + `{ state: "running" }`。
- 背景工作**必須自開 DI scope**:`LibraryScanner` / `PmDbContext` 是 Scoped,而請求 scope 在 `POST` 回應後即釋放 → 用 `IServiceScopeFactory.CreateScope()` 在背景解析 `LibraryScanner`(比照 `TaggingWorker`)。`enqueueTagging` 沿用現行邏輯(未帶 → 跟隨 `Inference:Wd14:Enabled`)。
- 背景工作結束:成功 → set `done` + `Result`(`ScanResult`)+ `FinishedAt`;丟例外 → set `error` + `Error`(訊息)+ `FinishedAt`。**背景例外務必 try/catch 落進狀態**,不可吞掉成幽靈。

**(c) 狀態查詢端點**
- `GET /api/roots/{id}/scan-status` → `ScanStatus`(查無 → `{ state: "idle" }`)。`.WithTags("Roots")`。
- `Result` 攤平回現有 `ScanResult` 八欄(FilesSeen / NewPhotos / NewLocations / SkippedUnchanged / Errors / ThumbsGenerated / JobsQueued / MarkedMissing)。

> 實作風格二選一(留 writing-plans 定):①最簡 = `POST` 內 `Task.Run` + registry(夠用);②正式 = 開 `ScanWorker` BackgroundService + `Channel<long>`(更貼齊 TaggingWorker,排隊/可重開)。單人本機掃描頻率低,傾向 ①;但 ② 較一致。

### 前端(`src/Pm.Web/.../manage/roots` + `manage.store` + `pm-api`)

- `pm-api` 加 `scanStatus(rootId): Promise<ScanStatus>`;既有 `scan(rootId)` 改為「秒回 202」語意(回傳型別調整)。
- `manage.store.rescan(id)`:POST 啟動 → **每 ~2s 輪詢 `scanStatus(id)`** 直到 `state !== 'running'`;`done` → `toast.success` 報「新增 X · 搬移… · 失蹤 Y · 縮圖 Z · 排標 W」(取 ScanResult 欄位);`error` → `toast.error(Error)`;結束清 `scanning` signal、停輪詢。輪詢 interval 以 `DestroyRef` 清理。
- `roots.ts onRescan`:加 `.catch`(POST 啟動失敗也要清 `scanning` + toast,治原 #1「永卡掃描中」);長掃描由輪詢承接,不再吊請求。
- 進頁時可選:對 `running` 中的 root 自動接續輪詢(若使用者重整頁面後掃描仍在背景跑)。**本案做最小版**:onRescan 觸發才輪詢;「重整後接續顯示」列加分項。

## 不做(YAGNI / 後續)

- **細部進度條(已掃 N/總 M)** —— 需 `ScanRootAsync` 邊掃邊回報進度(目前只回最終 `ScanResult`),改動較大,後續加分。
- **SignalR/WebSocket 即時 push** —— 與「不引 broker」取向衝突,輪詢已夠。
- **掃描狀態持久化 / 跨重啟接續** —— 記憶體即可,重掃 idempotent。
- **取消掃描(CancellationToken 串到端點)** —— 後續可加。
- 全域「所有 root 掃描總覽」端點 —— 目前 per-root 查詢即可。

## 驗收

- `dotnet build` + `dotnet test` 全綠;`ng build` 0 錯、前端 52 測試綠。
- 手測:對一個大 root 按「重新掃描」→ **POST 秒回**、按鈕進「掃描中…」、頁面不吊;掃描期間 `GET scan-status` 回 `running`;完成後 `toast` 報 `ScanResult` 計數、按鈕復原;模擬失敗(如不存在路徑的 root)→ 狀態 `error` + toast 報錯,按鈕不卡死。
- 並發:對同一 root 連點兩次「重新掃描」→ 不啟動第二次掃描。
- 背景例外不靜默:強制讓掃描丟例外 → 狀態為 `error` 且帶訊息(非永遠 running)。

## 對齊鐵則 / 慣例

- 純後端非同步化 + 前端輪詢接線;**不改掃描邏輯本身、不碰原圖、不改 SQLite canonical**。WAL 已開,背景掃描與 API 雙寫入無互卡(同 TaggingWorker)。
- 後端 TDD(背景狀態轉移、並發守衛可測);前端 UI 改完 `ng build` + 起 app 手測。
- 小切片、逐步 commit。
