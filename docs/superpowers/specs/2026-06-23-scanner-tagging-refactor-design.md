# LibraryScanner 重構 + Tagging 解耦 — 設計文件

- 日期:2026-06-23
- 狀態:**Slice 1a/1b 已實作;1c+ 待實作**(切片順序見 §7)
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
3. **Slice 1c:missing 對帳驗證**:
   - 先撈 EF Core 10 對 `!seenPaths.Contains(l.RelPath)` 的實際 SQL。若走 `json_each(@param)` 單 JSON 參數,正確性上不撞 SQLite 參數上限;僅評估效能。
   - 若實測有 SQL/效能問題,再導入 scan token 或 chunk 對帳;不預設這段一定壞,因為它是現存行為而非本重構新增。
4. **記憶體 / change tracker**:Slice 1a 只 track location + scalar file size,避免 `Photo`/Exif 膨脹。Slice 1b 已把 slow path SaveChanges 收斂到每批 1-2 次,且批次後釋放 slow-path 產生/改動的 entity tracking。混合掃描仍會 track root 內既有 location,後續若實測慢再用更細的投影/attach 策略處理。

預期:十萬檔全掃由分鐘級降到秒~十幾秒。**行為不變的重構**,既有 Scanner 測試是驗收主軸。

## B. 掃描 / Tagging 解耦(動作層)

1. **掃描排 job 改可選**:`ScanRootAsync(..., bool enqueueTagging = …)`,掃描專注「身分/位置/縮圖」索引;是否排 tag 可關。
2. **新增排程端點**(滿足「單獨/指定幾個檔案進排程」):
   - `POST /api/photos/{id}/tag` — 單張(重)標。
   - `POST /api/tag/requeue` — body 指定 `photoIds[]` 或條件(`未標的` / `error 的` / `整個 root` / `全部重標`)。
3. **支援重標、不動 schema**:`requeue` 把目標 photoId 的 job `state` 設回 `pending`(upsert,沿用 PhotoId PK)。worker `ProcessNextAsync` 只撈 `pending`,天然重跑。
   - `retry`:錯誤/中斷重試,不清舊 tag。
   - `refresh`:重跑 WD14 前先刪該 photo 的 `source='wd14'` photo_tag,再寫入新結果;換 threshold/model 時用此模式,避免舊 tag 永遠殘留。
4. **失敗 job 退避重試**(順帶,原 handoff C8):`error` job 可被 requeue 重排;是否自動重排列為可選。

## C. 推論開關拆分(能力層,Q1)

- 由單一 `Inference:Enabled` 改為**各模型獨立子開關**,沿用「預設關、免下載、零開銷」精神:

```jsonc
"Inference": {
  "Wd14": { "Enabled": false, "Backend": "directml", "ModelDir": "...", "GeneralThreshold": 0.35, "CharacterThreshold": 0.85 },
  "Clip": { "Enabled": false, "Backend": "directml", "ModelDir": "..." }
}
```

- `AddWd14Tagging` 改讀 `Inference:Wd14:Enabled`;未來 `AddClipEmbedding` 讀 `Inference:Clip:Enabled`。`IInferenceSessionFactory` 已抽象可共用,tagger/worker 各自獨立。
- 能力層由 `appsettings` / Rider launchSettings 啟動參數控制(改了要重啟,合理)。

## D. 行為層持久化(Q2)— **後續,待前端設定頁需求出現**

- 需要「免重啟」的執行期開關(自動標 on/off、worker 暫停/恢復、手動觸發重標)時,新增一張輕量 `app_setting`(key-value)表 + 設定端點 + 前端設定頁。
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
- B(解耦):`enqueueTagging=false` 不排 job、requeue 把 done/error 設回 pending、指定 photoIds 只排這些;`refresh` 模式清掉舊 `wd14` photo_tag 後再重寫。
- C(開關):`Wd14:Enabled=false` 不註冊 worker;`true` 才註冊(比照現有 `Wd14SetupTests`)。

## 切片計畫(slice-and-commit,序列)

1. **切片 1a**:快路徑大量重掃 — 批次載入 location + photo file size dict;未變檔只批次更新 `LastSeenAt`;新檔/變更檔邏輯先不動。**已完成**。
2. **切片 1b**:初次匯入/大量新檔 — chunk hash → 批次查 photo by hash → 同批 hash 去重 → 兩階段批次新增 photo+location+job。**已完成**。
3. **切片 1c**:missing 對帳 — 驗 EF Core 10 實際 SQL;必要時 scan-token 或 chunk 對帳。
4. **切片 2**:掃描排 job 改可選(B1)。
5. **切片 3**:requeue / 指定 photoId 排程端點(B2/B3),含 `retry` / `refresh` 語意。
6. **切片 4**:Wd14/Clip 開關拆分(C)。
7. (後續)行為層 app_setting + 前端設定頁(D)。

每切片:後端走 TDD、build + 全測試綠後 commit。動到設計決策時同步更新 `2026-06-21-picture-management-design.md` 與本檔。
