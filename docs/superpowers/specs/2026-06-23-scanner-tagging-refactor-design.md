# LibraryScanner 重構 + Tagging 解耦 — 設計文件

- 日期:2026-06-23
- 狀態:**Slice 1a/1b/1c/2/3 已實作;Slice 4+ 待實作**(切片順序見 §7)
- 關聯:`CLAUDE.md` 鐵則 #1/#2(就地索引、hash 是身分、不碰原檔)、#5(tag 來源要分)、#6(ONNX/DirectML 抽象)、#7(`tagging_job` 程序內佇列);
  吸收 `2026-06-22-scan-detection-design.md` 的**路線 A**(掃描效能);現有實作 `src/Pm.Scanner/LibraryScanner.cs`、`src/Pm.Api/Wd14Setup.cs`、`src/Pm.Api/TaggingWorker.cs`
- 取代範圍:本文**吸收** scan-detection-design 的路線 A1(批次消 N+1)並擴充;該文的**路線 B**(FileSystemWatcher 即時偵測)仍留在原文,屬更後續。

## 背景與問題

讀碼確認三個現況問題,彼此相關,合併處理:

1. **掃描效能(read N+1 + write 逐檔 commit)** — `LibraryScanner.ScanRootAsync`:
   - 每檔一次 `PhotoLocations.Include(Photo).FirstOrDefaultAsync`(`LibraryScanner.cs:45`)→ N 次 DB read。
   - 每檔多次 `SaveChangesAsync`(`:55`/`:78`/`:92`/`:120`)→ N 次 commit;尤其**快路徑(沒變的檔)也逐檔 commit** 只為更新 `LastSeenAt`,十萬量級為最大瓶頸。
   - 初次匯入/大量變更檔還有逐檔 `Photos.FirstOrDefault(FileHash==hash)`;初次匯入主成本仍是 hash I/O,但 indexed SELECT 與逐檔 transaction 仍要在第二切片收斂。
2. **掃描與 tagging 耦合** — 掃描發現新 photo 且可解碼時,**同流程**直接 `TaggingJobs.Add`(`LibraryScanner.cs:91`)。沒有「只索引不標」或「對指定圖批次標」的路徑。
3. **無法重標 + 無手動排程** — `TaggingJob.PhotoId` 是 PK(`TaggingJob.cs:5`),一張圖只能一筆 job,`done` 後無法再排;`Program.cs` 也沒有任何手動排 tagging / requeue 端點。

另:推論開關 `Inference:Enabled`(`Wd14Setup.cs:13`)是**單一總閘**,未來加 CLIP 會被迫與 WD14 綁同一開關。

## 核心原則:能力 / 行為 / 動作 三層分離

目前系統把這三層黏在一起,本重構的主軸是拆開:

| 層 | 是什麼 | 放哪 | 改了要重啟? |
|---|---|---|---|
| **能力層** | 載不載模型、跑不跑 worker | `appsettings` / launchSettings(啟動參數) | ✅ 要 |
| **行為層** | 自動標 on/off、暫停/恢復佇列 | `app_setting` 表 + 執行期 API + 前端設定頁 | ❌ 不可要 |
| **動作層** | 掃描 / 標籤 / 重標,可指定範圍 | `LibraryScanner` 職責拆分 + tagging 排程端點 | ❌ |

三層拆開後目標場景成立:模型常載(能力開)→ 平時自動標(行為開)→ 某天換 threshold 重標某資料夾(動作層 requeue 指定 root)→ 標前先暫停觀察(行為層 pause)。

## A. 掃描效能重構(動作層基礎,吸收 scan-detection 路線 A1)

**read 與 write 一起修**,否則消了 read N+1 仍被 EF change tracker 在每次 `SaveChanges` 掃全 tracked 集拖累。分三個小切片做,避免一次重寫整個 scanner:

1. **Slice 1a:快路徑大量重掃**:
   - 開掃前一次載入 root 內所有 location,但只 track `PhotoLocation`,並投影 `Photo.FileSize` scalar → `Dictionary<relPath, { Location, PhotoFileSize }>`,迴圈內查 dict O(1),取代 `:45-47` 的 per-file location query。
   - 不用 `Include(Photo)`:快路徑只需要 file size,避免把 `Photo.Exif` 等大欄位與十萬級 `Photo` entity 放進 change tracker。
   - 未變檔只更新 `LastSeenAt`,累積後批次 `SaveChanges` 一次;新檔/變更檔邏輯先維持現狀。
   - 目標是先解決「已索引十萬圖庫重掃」的最大痛點,低風險且既有測試能保行為。
2. **Slice 1b:初次匯入/大量新檔 chunk pipeline**:
   - 已實作:slow path 先收集每批需 hash 的檔案(`SlowPathBatchSize=500`),批次 hash 後用 `Where(p => hashes.Contains(p.FileHash))` 一次查既有 photo。
   - 已實作:同批 `hash -> Photo` map 去重,避免同批 duplicate hash 重複建 photo。
   - 已實作:新增 photo 先批次 `SaveChanges` 取得 id,再批次新增 location/job;新檔 3 筆含 1 組 duplicate 的測試由 5 次 SaveChanges 降為 2 次。
   - 保留:縮圖仍在批次內逐張生成,但 job/location 寫入已批次化。
   - 已實作:批次查既有 photo 使用 `AsNoTracking`;每批 slow path 儲存完成後 detach 該批新增/更新的 `Photo` / `PhotoLocation` / `TaggingJob`,避免初次匯入時 change tracker 隨檔案數無界成長。測試以 501 檔跨 batch 初次匯入驗證 scanner context 不殘留批次匯入實體。
3. **Slice 1c:missing 對帳(已實作)**:
   - **驗證結果(實機)**:EF Core 10 + SQLite 對 `!seenPaths.Contains(l.RelPath)` **不走** `json_each`,而是逐元素 `RelPath NOT IN (@p1...@pN)`;`ToQueryString` 在 5 與 2000 元素都同樣展開,且實機執行 100,000 元素直接丟 `SqliteException: 'too many SQL variables'`(SQLite 變數上限 32766)。故十萬量級圖庫掃描必在對帳步驟崩潰 —— 是現存潛在 bug,被本重構的規模目標觸發。
   - **修法(無 schema 變更)**:重用開掃已載入的 `locationsByPath`(已含該 root 全部 location),在記憶體算出 `present 且未在 seenPaths` 的 missing 集合,再以 location id `Chunk(10_000)` 分塊 `ExecuteUpdateAsync`。seenPaths 比對全程在記憶體(`HashSet.Contains`),不再進 SQL;唯一進 SQL 的 IN 是 missing id 分塊(≤10k 參數/塊,穩在上限內)。
   - **測試**:`Reconcile_marks_missing_above_sqlite_variable_limit_without_crashing` —— seed 33,000 present location、掃空 root,全數轉 missing 不崩(守住分塊 Id 更新路徑;快版 8s)。大量 seenPaths 的 faithful 情境(33k 實體檔)開發時已實機跑過一次綠燈,因 ~6m 過慢不留進常態套件。
4. **記憶體 / change tracker**:Slice 1a 只 track location + scalar file size,避免 `Photo`/Exif 膨脹。Slice 1b 已把 slow path SaveChanges 收斂到每批 1-2 次,且批次後釋放 slow-path 產生/改動的 entity tracking。混合掃描仍會 track root 內既有 location,後續若實測慢再用更細的投影/attach 策略處理。

預期:十萬檔全掃由分鐘級降到秒~十幾秒。**行為不變的重構**,既有 Scanner 測試是驗收主軸。

## B. 掃描 / Tagging 解耦(動作層)

1. **掃描排 job 改可選(已實作)**:
   - `ScanRootAsync(long rootId, bool enqueueTagging = true, CancellationToken ct = default)`。預設 `true`(行為不變);`false` → 掃描專注「身分/位置/**縮圖照產**」,只跳過 `tagging_job`。
   - 端點 `POST /api/roots/{id}/scan?enqueueTagging=`:未帶 query → 跟隨能力旗標 `Inference:Wd14:Enabled`。推論關時預設**純索引、不堆死 job**;`?enqueueTagging=true` 可在推論關時 **pre-queue**(待之後啟用 worker 一次消化),`?=false` 強制只索引。
   - **三層 UI 約定(給 Slice D 前端設定頁)**:能力層關閉(模型未載)時,行為層的「**自動標排程** on/off」toggle 應**反灰不可選** + 提示需到啟動設定開 `Inference` 並重啟 —— 因為沒有 worker,開了也無引擎。動作層的 pre-queue(`?enqueueTagging=true`)是**獨立的明示動作**(API 或日後獨立按鈕「掃描並預先排入佇列」),**不可**與那顆自動排程 toggle 共用。
2. **requeue / 重標端點(B2,Slice 3 — 已實作;操作「已索引的圖」,不碰檔案系統)**:
   - `POST /api/tag/requeue` — 批次,body `{ mode, scope }`。
   - `POST /api/photos/{id}/retag?mode=` — 單張(等同 requeue 的 `scope = 該 id`)。**命名刻意避開既有 `POST /api/photos/{id}/tags`(手動加標籤),別只差一個 `s`。**
   - **mode(2 軸組合 = 清舊 `wd14` tag? × 重排 job?)**:

     | mode | 清舊 `wd14` tag | 重排 job | 用途 |
     |---|---|---|---|
     | `retry` | ✗ | ✓ | 失敗/中斷重跑 |
     | `refresh` | ✓ | ✓ | 換 threshold/model 重標 |
     | `clear` | ✓ | ✗ | 放棄 WD14 自動標(只留 manual/path) |

   - **scope(動態條件,請求當下解析成一組 photoId)**:四選一,不可同時指定多個 scope;`photoIds: long[]`(明示)/ `error`(只重排失敗的)/ `root: long`(整個 root)/ `all`(全部)。
     - `clear` + `scope: all` = 整庫全清自動標;`clear` + `scope: root` = 只清某資料夾的自動標。
3. **機制、不動 schema**:
   - **job upsert(沿用 `tagging_job` PhotoId PK,已實作)**:有 job → `State=pending`、`Attempts=0`;**沒有 job → 新建 pending**(Slice 2「只索引不排」的圖靠這補建)。worker `ProcessNextAsync` 只撈 `pending`,天然重跑。`clear` 不 upsert job。
   - **refresh / clear 清舊 tag(已實作)**:`DELETE photo_tag WHERE photo_id ∈ 目標 AND source='wd14'` —— **只清被 requeue 的那批,不無差別全庫**;`manual` / `path` tag 不動。
   - **大量範圍分塊(1c 教訓,已實作)**:`scope = root / all` 可能十萬量級,job upsert 與 tag delete 都要**分塊**(≤10k/塊),不可單一巨大 `IN`。
   - **能力層互動**:requeue 屬動作層,worker 沒開也照排(pre-queue,等之後啟用再消化),與 Slice 2 一致。
4. **失敗 job 退避重試**(順帶,原 handoff C8):`error` job 可被 requeue(`mode=retry, scope=error`)重排;是否**自動**退避重排列為可選、暫不做。

> **責任邊界(B.1 掃描 vs B.2 requeue —— 別搞混)**
> - **掃描按鈕(B.1,Slice 2)**:`POST /api/roots/{id}/scan`。職責 = **檔案系統 → DB 同步**(走訪磁碟、算 hash、建/更新 photo+location+縮圖、對帳 missing)。其中「排 tagging job」只是針對**這次新發現/變更的圖**的副作用,由 `enqueueTagging` 控制。**它不重標已索引的圖。**
> - **requeue / retag(B.2,Slice 3)**:`/api/tag/requeue`、`/api/photos/{id}/retag`。職責 = 對**已經在庫裡的圖**,**按需**(重)排 WD14 標籤 / 清舊 tag。**完全不碰檔案系統、不掃磁碟。**
> - 兩者都會「產生 tagging job」,但觸發點不同:掃描是「發現新檔順手排」,requeue 是「對既有圖手動重排」。UI 上應是兩個不同入口,不要共用:**掃描**在資料夾上方那顆;**單張 retag/clear** 落在「點開圖、檢視器的 tag 視窗」內(per-photo);批次 requeue 屬維護動作另設入口。

### 邊界與已知限制(Slice 3 定案,review 後補)

1. **`photo_tag` 不支援多 source(現有 schema 限制,本 slice 不解但承認)**:PK = `(photo_id, tag_id)`(`PmDbContext.cs:96`),一個 (圖, tag) 只有一筆、一種 source。`refresh`/`clear` 刪 `source='wd14'` 只動 wd14 那筆;若某 tag 已是 `manual`/`path`,**不會被刪**,且 worker 重標到同一 tag 時 `AttachTagAsync` 因 `(photoId, tagId)` 已存在而**略過 → 不改回 wd14、不記 confidence**(manual 策展優先,合鐵則 #5 精神)。→ 測試要涵蓋「manual tag 經 refresh 後仍在、不被清、不重複」。
2. **`scope = root / all` 只取 present**:解析 scope 時只選**至少有一個 `present` location** 的 photo;`missing`/`archived` 不重排 —— 否則 worker `ResolvePathAsync`(只找 present path)會回 null,job 直接變 `error`。
3. **`scope = error` 僅配重排 mode**:`error` 的語意是「重跑失敗的」,只對 `retry`/`refresh` 有意義;`clear + error`(清完不排)語意怪 → **400 拒絕**。`clear` 只配 `photoIds` / `root` / `all`。
4. **running job**:upsert 不論現狀一律設 `pending`(= 重新排入佇列),**不取消正在跑的推論**。若剛好在跑,該輪可能仍寫回一次,之後 pending 會再被撈一次重跑;單 worker 下可接受(啟動本就有 `running→pending` 回收)。文件用語固定為「重新排入」,非「取消推論」。
5. **回傳形狀**:`{ matched, clearedTags, jobsCreated, jobsUpdated }` —— UI 才知道按下去實際影響幾張 / 清幾筆 / 新建與更新各幾筆 job。

### 實作順序(service-first,TDD)

1. 先做薄 service(`TaggingScheduler` 或擴 `TagService`),**不直接塞 `Program.cs`**;端點只解析 scope→photoId 集合再委派。
2. TDD `retry`:done/error/running job → pending、`Attempts=0`;**無 job 補建** pending。
3. `refresh`:只刪目標 photo 的 `wd14` tag、`manual`/`path` 不動 → job pending;**manual tag 倖存**(限制 1)。
4. `clear`:刪 `wd14`、**不**建 job。
5. `scope=root` 只挑 present 的 photo。
6. 大量 scope(>32k)分塊,不撞 SQLite 變數上限(沿用 1c 教訓)。
7. 驗 `clear + error` 回 400;多個 scope 同時指定回 400。

## C. 推論開關拆分(能力層,Q1)— **已實作(Slice 4)**

- 由單一 `Inference:Enabled` 改為**各模型獨立子開關**,沿用「預設關、免下載、零開銷」精神。**乾淨重命名,不留舊鍵 fallback**;`Enabled` 與 `Backend` 都移進各模型節點下:

```jsonc
"Inference": {
  "Wd14": { "Enabled": false, "Backend": "directml", "ModelDir": "...", "GeneralThreshold": 0.35, "CharacterThreshold": 0.85 },
  "Clip": { "Enabled": false, "Backend": "directml", "ModelDir": "..." }
}
```

- `AddWd14Tagging` 已改讀 `Inference:Wd14:Enabled` + `Inference:Wd14:Backend`;未來 `AddClipEmbedding` 讀 `Inference:Clip:*`。`IInferenceSessionFactory` 已抽象可共用,tagger/worker 各自獨立。
- 能力層由 `appsettings` / Rider launchSettings 啟動參數控制(改了要重啟,合理)。Slice 2 的 scan enqueue 預設也已一併改讀 `Inference:Wd14:Enabled`;`appsettings.json` 與 launchSettings 同步遷移。

## D. 行為層持久化(Q2)— **後續,待前端設定頁需求出現**

- 需要「免重啟」的執行期開關(自動標 on/off、worker 暫停/恢復、手動觸發重標)時,新增一張輕量 `app_setting`(key-value)表 + 設定端點 + 前端設定頁。
- **UI 約定見 §B.1**:能力層(`Inference:Wd14:Enabled`)關閉時,前端「自動標排程」toggle 反灰不可選;pre-queue 屬動作層,獨立明示,不共用此 toggle。
- **現階段不急**:單人開發 + 測試,能力層用 launchSettings、動作層用 requeue 端點即足。app_setting 等真要做前端二次開關時再加。

## DB schema 影響

- **本重構不動 schema**:`tagging_job`(PhotoId PK)沿用;requeue = `state` 設回 pending。
- 現階段 `tagging_job` 明確只代表 **WD14 tagging job**。未來 CLIP 排程落地時再評估 `(photo_id, kind)` composite key 或 WD14/CLIP 獨立 table,避免一張 photo 只能同時有一種 job。
- 行為層 D 若實作才 +1 張 `app_setting` 表 + migration。

## 對齊鐵則

- #1/#2:掃描純讀取 + 就地索引,不搬/不改原檔;hash 仍是身分。
- #5:tag 來源不變(`source` ∈ path/manual/wd14)。
- #6:推論仍走 `IInferenceSessionFactory`,開關拆分不動 EP 抽象。
- #7:`tagging_job` 仍是程序內 DB-backed 佇列;requeue 不引 broker、不開第二程序。

## 測試考量(本專案慣例)

- 後端測試 DB 隔離 = **每測試獨立 SQLite 檔(`Data Source={tmp}`)或 `:memory:`**(見 `EnrichTests.cs`/`SchemaTests.cs`),非交易回滾。新測試沿用此慣例避免互相污染。
- A(效能):**行為不變重構**,既有 Scanner 測試全綠為主軸;補窄測「快路徑不逐檔 commit」與「初次匯入批次後不殘留 slow-path tracking」,用可觀察的 `SaveChangesAsync` 次數 / change tracker entries 守住性能邊界,避免把一般行為測試寫成實作細節大網。
- B(解耦):`enqueueTagging=false` 不排 job、requeue 把 done/error 設回 pending、指定 photoIds 只排這些、**無 job 的圖(Slice 2 index-only)requeue 補建 pending**;`refresh`/`clear` 清掉該批 `wd14` photo_tag(不碰 manual/path),`clear` 清完不重排;大量 scope(>32k)不撞 SQLite 變數上限(分塊)。
- C(開關):`Wd14:Enabled=false` 不註冊 worker;`true` 才註冊(比照現有 `Wd14SetupTests`)。

## 切片計畫(slice-and-commit,序列)

1. **切片 1a**:快路徑大量重掃 — 批次載入 location + photo file size dict;未變檔只批次更新 `LastSeenAt`;新檔/變更檔邏輯先不動。**已完成**。
2. **切片 1b**:初次匯入/大量新檔 — chunk hash → 批次查 photo by hash → 同批 hash 去重 → 兩階段批次新增 photo+location+job。**已完成**。
3. **切片 1c**:missing 對帳 — 實機證實 `NOT IN (seenPaths)` 在 >32766 撞 SQLite 變數上限;改記憶體 set-diff + 分塊 Id 更新(無 schema 變更)。**已完成**。
4. **切片 2**:掃描排 job 改可選(B1)— `enqueueTagging` 參數 + 端點綁能力旗標、`?enqueueTagging=` 可覆寫。**已完成**。
5. **切片 3**:requeue / retag 端點(B2/B3)。3 mode(`retry` / `refresh` / `clear`)× 4 scope(`photoIds` / `error` / `root` / `all`);job upsert(無則補建)、refresh/clear 清該批 `wd14` tag、大量分塊。**已完成**。
6. **切片 4**:Wd14/Clip 開關拆分(C)— `Inference:Enabled`/`Backend` 乾淨重命名為 `Inference:Wd14:Enabled`/`Backend`;3 prod 讀取點 + appsettings + launchSettings + 測試同步。**已完成**。
7. (後續)行為層 app_setting + 前端設定頁(D)。

每切片:後端走 TDD、build + 全測試綠後 commit。動到設計決策時同步更新 `2026-06-21-picture-management-design.md` 與本檔。
