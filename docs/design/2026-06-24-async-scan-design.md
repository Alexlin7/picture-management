# 掃描非同步化 + 掃描資料完整性修正(背景跑 + 前端輪詢)— 設計文件

- 日期:2026-06-24
- 狀態:**核心已實作並測試通過**(2026-06-24):掃描非同步化、`scan-status`、前端輪詢、batch 原子化、縮圖缺檔補產、thumb serve/write 硬化、SQLite `busy_timeout` 已落地。**待補/待決策:**孤兒 photo 清理策略、roots 頁 per-root「重產縮圖」維護入口。
- 範圍:① 掃描非同步化(背景 + 輪詢);② 掃描資料完整性修正(原子 batch / 縮圖補產脫鉤 / serve 硬化 / SQLite busy_timeout;孤兒清理待決策)+ roots 頁「重產縮圖」維護入口(待補)。②由 2026-06-24 實機事故催生(68 張缺縮圖),根因分析見「資料完整性修正」段。
- 關聯:`src/Pm.Api/Program.cs`(`POST /api/roots/{id}/scan`)、`src/Pm.Scanner/LibraryScanner.cs`(`ScanRootAsync`)、`src/Pm.Api/TaggingWorker.cs`(既有背景服務模式,可參照)、`src/Pm.Web/.../manage/roots`(前端)
- 起因:`POST /api/roots/{id}/scan` 目前**同步阻塞** —— 後端 `await scanner.ScanRootAsync(...)` 把「走訪整棵樹 + 每檔 SHA-256 + EXIF + 縮圖 + 對帳」全跑完才回應。十萬量級首次掃描可能數分鐘~數小時,整條 HTTP 請求吊著:前端按鈕卡「掃描中…」無進度;任何中間層(反向代理 `proxy_read_timeout`、瀏覽器、Kestrel)先 timeout → 504/連線斷,但後端其實還在掃 → 前後端狀態分裂。
- **2026-06-24 實機事故(本案擴充範圍):** 使用者新增資料夾掃描、中途切頁,事後發現「id 5194 之後的圖沒縮圖」。調查(讀 `pm.sqlite` + `thumbs/`)實證:真實損害 = **68 張 present 圖缺縮圖檔(id 5194–5261 連續尾巴)**,另殘 1 隻無 location 的孤兒 photo。**並非** client-abort 取消掃描(端點沒接 `CancellationToken`,切頁不觸發);而是掃描中斷後**重掃補不回縮圖** + **batch 非原子**留孤兒。詳見下方「資料完整性修正」。同步掃描卡住請求正是誘使使用者「以為當掉 → 中止/重建程序」進而腰斬 batch 的溫床,故與本案一併處理。

## 決策

- **掃描改非阻塞**:`POST` 啟動背景掃描後**立刻回 `202`**,前端**輪詢**狀態端點得知進度/完成。
- **通知用輪詢,不引 SignalR/WebSocket** —— 與既有 WD14 佇列輪詢(`GET /api/tagging/stats`,gallery 4s 輪詢)一致,守住「單程序、不引 broker」取向。即時 push 與細部進度條列為後續加分。
- 把原健壯性清單 **#1(roots「重新掃描」無 `.catch`、無完成回饋)併入本案** —— 因同樣改 roots 前端,且非同步化後「卡死」根因即解。

## 設計

### 後端

**(a) 掃描狀態登記(記憶體,單程序)**
- 已新增可注入 singleton `RootScanCoordinator`,以 lock + dictionary 登記 per-root `RootScanStatus`。
- `RootScanStatus { long RootId; string State; ScanResult? Result; string? Error; DateTimeOffset? StartedAt; DateTimeOffset? FinishedAt; }`;`State ∈ idle | running | completed | error`。查無 entry = `idle`。
- **記憶體即可**:重啟時 running 狀態與掃描任務一起消失 → 狀態回 idle;重掃本來就 idempotent,無資料風險。本 spec 不做狀態持久化(YAGNI)。

**(b) `POST /api/roots/{id}/scan` 改非阻塞**
- 並發守衛:若該 root 已 `running` → **不重複啟動**,回目前狀態(避免雙擊/重入)。check-and-set 需原子(`TryAdd` 或鎖)。
- 否則:標 `running`(記 `StartedAt`)→ **啟動背景工作** → 立刻回 `202 Accepted` + `{ state: "running" }`。
- 背景工作**必須自開 DI scope**:`LibraryScanner` / `PmDbContext` 是 Scoped,而請求 scope 在 `POST` 回應後即釋放 → 用 `IServiceScopeFactory.CreateScope()` 在背景解析 `LibraryScanner`(比照 `TaggingWorker`)。`enqueueTagging` 沿用現行邏輯(未帶 → 跟隨 `Inference:Wd14:Enabled`)。
- 背景工作結束:成功 → set `completed` + `Result`(`ScanResult`)+ `FinishedAt`;丟例外 → set `error` + `Error`(訊息)+ `FinishedAt`。**背景例外務必 try/catch 落進狀態**,不可吞掉成幽靈。

**(c) 狀態查詢端點**
- `GET /api/roots/{id}/scan-status` → `ScanStatus`(查無 → `{ state: "idle" }`)。`.WithTags("Roots")`。
- `Result` 攤平回現有 `ScanResult` 八欄(FilesSeen / NewPhotos / NewLocations / SkippedUnchanged / Errors / ThumbsGenerated / JobsQueued / MarkedMissing)。

> 已採 ①:`RootScanCoordinator.TryStart` 內啟動 `Task.Run` + 自開 DI scope。單人本機掃描頻率低,目前不引入 Channel/BackgroundService。

### 前端(`src/Pm.Web/.../manage/roots` + `manage.store` + `pm-api`)

- `pm-api` 加 `scanStatus(rootId): Promise<ScanStatus>`;既有 `scan(rootId)` 改為「秒回 202」語意(回傳型別調整)。
- `manage.store.rescan(id)`:POST 啟動 → **每 ~4s 輪詢 `scanStatus(id)`** 直到 `state !== 'running'`;`completed` → `toast.success` 報新增/位置/縮圖計數(取 ScanResult 欄位);`error` → `toast.error(Error)`;結束清 `scanning` signal、停輪詢。
- `roots.ts onRescan`:加 `.catch`(POST 啟動失敗也要清 `scanning` + toast,治原 #1「永卡掃描中」);長掃描由輪詢承接,不再吊請求。
- 進頁時可選:對 `running` 中的 root 自動接續輪詢(若使用者重整頁面後掃描仍在背景跑)。**本案做最小版**:onRescan 觸發才輪詢;「重整後接續顯示」列加分項。

## 資料完整性修正(2026-06-24 事故根因,併入本案)

兩條根因都在 `LibraryScanner.ScanRootAsync` 的 batch 流程:

**根因 A — 縮圖只在「新 photo」產(漏補產)。** 縮圖在 `:159-180` 的 `newPhotosByHash` 迴圈內產;photo 一旦已存在(dedup,`photosByHash.ContainsKey` 命中)就跳過。第一次掃描中斷(photo 已落地、縮圖只到 5193)後**重掃**,5194–5261 因 hash 已存在被去重 → location 補上了、縮圖卻**永遠補不回**。→ 68 張破圖的直接原因。

**根因 B — batch 非原子(photo 與 location 分兩次 commit)。** `:157` 先存 photo(交易 A,獨立 commit 完成)、`:215` 才存 photo_location(交易 B)。兩交易間任何中斷(程序被結束/重建、未捕捉例外、日後接 cancellation)→ photo 已落地、location 沒寫 → 孤兒 photo。本次自我修復後僅殘 1 隻,但結構缺陷在。

修正:

1. **縮圖補產脫鉤「新圖」**:改判據為「present 且可解碼、但 `thumbs.PathFor(hash)` 不存在 → 產」。在 batch upsert location 時對該 photo 檢查縮圖檔存在性,不存在就產(不論新建或既有)。如此**重掃天然補回漏縮圖**,事故不再復發。
2. **batch 原子化**:同一 batch 的 photo 與其 photo_location 包進**單一交易**(`db.Database.BeginTransactionAsync()` 框住 `:157`+`:215`,或重排成單次 `SaveChanges` 同含 photo+location)。縮圖一律**放交易外/後**(失敗本就容錯、可由 #1 重產,不該卡在交易裡)。中斷 → photo+location 一起 rollback → 不留孤兒。
3. **孤兒清理(一次性 + 防衛,待決策)**:掃出「無任何 location 的 photo」→ 刪除(連同其縮圖檔);本次殘留 id 4911 一併清。因這是 DB 刪除行為,實作前需確認採「啟動自檢」、「維護端點」或「僅手動 SQL/工具」。
4. **縮圖 serve 硬化**(治使用者實遇的 `IOException: 檔案被另一程序占用`):`Program.cs` thumb 端點開檔改用 `FileShare.ReadWrite`(自開 `FileStream` + `Results.Stream`,或等義),`IOException` 時短重試一次再回 503(非 500 破圖);寫縮圖端用 **temp + 原子 rename**(① 已採此法),讓 serve 永不讀到半檔。
5. **SQLite 並發硬化 — 設連線層 `busy_timeout`**(治使用者實遇的 `SQLite Error 5: database is locked`):連線字串/連線開啟時設 `PRAGMA busy_timeout`(經 `DbConnectionInterceptor` 在 `ConnectionOpened` 跑 `PRAGMA busy_timeout=5000;`,或連線字串對等設定)。
   - **根因**:`Program.cs:10-11` 連線僅 `Data Source=pm.sqlite`,**未設 `busy_timeout`**。`:41-42` 註解的假設有誤 —— `Command Timeout` 只在**命令執行**時對 `SQLITE_BUSY` 重試;但實遇錯誤發生在**連線歸還連線池**時(`RelationalDataReader.DisposeAsync → CloseAsync → SqliteConnection.Deactivate` 的清理 `ROLLBACK`),該語句不走 command-timeout 重試迴圈,WD14 worker 持寫鎖時即刻 BUSY。
   - WAL「讀不擋寫」但寫鎖之間仍序列化;`busy_timeout` 讓所有操作(含 teardown)在爭用時稍等重試而非立即炸。**順手修掉 `:41-42` 誤導註解。**

**維護入口(前端,roots 頁;待補):** 加一顆 **per-root「重產縮圖」** 按鈕(及/或全庫一顆)→ 呼叫新的 backfill 端點 `POST /api/roots/{id}/rebuild-thumbs`(掃該 root 下 present 且缺縮圖檔者重產;沿用非同步 + 輪詢狀態框架,與掃描同款 UX)。這是 #1 的手動補強:不必整檔重掃也能補縮圖,亦是日後縮圖規格變更(尺寸/格式)的重生入口。

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
- **完整性(回歸本次事故):** 已有測試覆蓋「掃描建 photo 後、寫 location 前中斷」→ 交易 rollback 不留孤兒;「present 但缺縮圖」的 photo 重掃 → 縮圖補回。`重產縮圖` 手動入口待補。thumb 讀寫改 FileShare.ReadWrite + temp 同目錄替換。

## 對齊鐵則 / 慣例

- 非同步化為純接線;**資料完整性修正(原子 batch / 縮圖補產脫鉤 / serve 硬化 / 孤兒清理)會動 `ScanRootAsync` 與 thumb 端點**,但嚴守鐵則:**不修改/搬動原圖、不寫 XMP、不改 SQLite canonical 真相、刪除走孤兒判定**。WAL 已開,背景掃描與 API 雙寫入無互卡(同 TaggingWorker)。
- 後端 TDD(背景狀態轉移、並發守衛可測);前端 UI 改完 `ng build` + 起 app 手測。
- 小切片、逐步 commit。
