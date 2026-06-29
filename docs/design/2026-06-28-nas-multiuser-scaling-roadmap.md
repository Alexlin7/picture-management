---
status: active
last-reviewed: 2026-06-29
supersedes: []
superseded-by: []
related: []
---

# NAS / 對外多人 —— 擴展瓶頸與演進路線圖

**狀態:前瞻性架構文件(forward-looking),非當前 backlog。** 產品當前定位仍是**單人 localhost**(見 `CLAUDE.md` 鐵則 8、主設計 `2026-06-21-picture-management-design.md` §2「單程序收斂」)。本文**不改變現狀、也不是 todo**;它記錄「若哪天走 NAS / 對外多人」時,**高併發瓶頸在哪、按什麼順序處理、每一步的觸發條件**,避免日後重新推導。

**日期:2026-06-28。來源:與使用者的架構討論(2026-06-28)。**

---

## 0. 前提與定位

- **現狀架構**:單一 .NET 程序 + 嵌入式 SQLite(已開 WAL)+ in-proc ONNX 推論 + 同機縮圖快取;只 bind `localhost`、無認證。
- 走 NAS 不代表推翻設計 —— 主設計早已把「日後 NAS / 多人」標為**可選路徑**,並預留兩個 seam:tagging 的無狀態 sidecar(鐵則 7)、儲存遷 Postgres+pgvector(Phase 2 / 多人)。本文把這些 seam 串成一條有觸發條件的路線。
- **integer PK 維持**(2026-06-28 討論結論):UUID 想解的(分散式產生、跨庫不撞、防列舉)在此要嘛不存在(單寫入、localhost)、要嘛**已由 `file_hash` 解掉**;改 PK 對最熱的 `photo_tag` join 表是淨損失(失去 SQLite `INTEGER PRIMARY KEY`=rowid 優化、鍵變大)。跨機合併以 `file_hash` 為準 remap surrogate id 即可。

---

## 1. 關鍵 reframe:account 系統把「寫入」收斂成政策上的單寫入者

對外多人 → 認證從可選變**必須**(鐵則 8)。一旦有帳號/角色:

- **策展類重寫入(掃描 / 匯入確認 / path rule / 批次 tag 編輯)收到 admin** → 寫入幾乎回到「一個 writer」,正是 SQLite + WAL 最舒服的型態(**一寫多讀**)。
- 公開使用者路徑**幾乎全是讀**(瀏覽 / 布林查詢 / 看縮圖)→ WAL 罩得住。
- **結論:「寫競爭」不是 NAS 版的主要瓶頸;瓶頸漂移到「讀 / 算的擴展」**,而那是比較標準、好解的問題。

兩個誠實的注解:

1. **仍存在一股背景寫:WD14 tagging worker**(admin 的掃描/匯入觸發,之後**非同步持續**寫 `photo_tag` / `tagging_job`)。但它是**單一 in-proc consumer、序列化、非使用者觸發** → 本質就是「那一個 writer」,跟一堆 reader 在 WAL 下並存沒問題。
2. **唯一會重新打開「多寫入者」的情境**:給一般使用者**個人化寫入**(我的最愛 / 個人 tag / 個人 saved search)。但那種寫又小又零星,人類操作頻率下不構成競爭(且可用 per-user 表隔離)。

---

## 2. 瓶頸地圖(由痛到不痛)

### B1 — SQLite 單寫入序列化
- **機制**:SQLite 全域只能一個 writer;WAL 讓**讀並發**、寫仍序列化。
- **admin-gating 後**:大幅降溫(見 §1),從「主瓶頸」退成「掃描期間的暫時鎖」。
- **殘留風險**:掃描(持寫鎖 + 全檔 hashing)與互動/背景寫互卡;NAS 慢碟放大寫延遲。
- **現有緩解**:WAL 已開(`StartupTasks`)、`busy_timeout` 已硬化(見 `2026-06-24-async-scan-design.md`)。
- **升級觸發**:多 admin 同時策展、個人化寫入普及、或寫入 p99 latency 超標 → 遷 Postgres(見 §3 Phase C)。

### B2 — in-proc 推論搶整台 box 的 CPU/GPU/RAM
- **機制**:ONNX 在同一程序內;tagging backlog 搶 CPU/GPU/RAM,可能餓死 API。NAS 常 CPU 弱、無像樣 GPU、RAM 小。
- **現狀緩解**:tagging 是**背景佇列、不在請求路徑** → 使用者延遲不直接中。
- **危險升級**:Phase 2 **CLIP 語意搜尋若上請求路徑**(每個 search 都要 image/text embedding 推論)→ 變每請求瓶頸。
- **既定解法**:tagging 拆**無狀態 sidecar**(POST 回 API、不直連 SQLite,鐵則 7),推論獨立擴縮、可與 API 分機;CLIP 同走 sidecar + **批次預算把 embedding 落 vector index**,查詢只比向量、不再即時推論。

### B3 — 縮圖生成 thundering herd
- **機制**:首次看圖要解碼 + resize(吃 CPU;大 PNG 吃 RAM);多人同時開「未產縮圖的圖牆」→ 解碼洪峰;若 lazy 生成**無 per-hash in-flight 鎖** → 同圖並發 miss 重複生成(白做工);並發大圖解碼可能 OOM 小 RAM NAS。
- **解法**:per-hash **single-flight 生成鎖**、縮圖當靜態檔交 reverse proxy / CDN 快取、限制並發解碼數 + 大圖記憶體上限、**掃描時預先批次產縮圖**(把成本挪離使用者請求)。

### B4 — 核心布林查詢的讀負載
- **機制**:`photo_tag`(十萬圖 → 數百萬列)上的布林多軸 + DAG 閉包(recursive CTE)+ facet 聚合;每次 search 是 count + page 兩查詢。
- **解法**:tag 閉包**快取**(reference data,改動才失效)、為每種查詢形狀備**覆蓋索引**、facet 聚合快取 / 物化;真的長大再上**讀複本**(Postgres read replica)。

### B5 — 掃描 vs 服務
- **機制**:掃描是重量級 bulk 寫(持寫鎖)+ 全檔 hashing(CPU/IO);多人時沒有「空檔」可挑。
- **解法**:admin 觸發 + **可排程離峰**;掃描分批 + 降 IO 優先;真要無痛 → 掃描/hash 移出主程序(配合 sidecar 思路)。

---

## 3. 演進路線圖(分階段 + 觸發條件)

> 原則:**每一步都由「實測痛點」觸發,不預先過度工程**。能停在哪一階段就停在哪。

**Phase 0(現狀):單人 localhost。** 維持,什麼都不動。

**Phase A — 「對外只讀 + admin 策展」最小可行版**
- 認證 + 角色(admin / user)+ **per-user 個人化表**(favorites / saved search 綁 user_id)。
- 縮圖 **single-flight 鎖** + reverse proxy 靜態快取 + **rate limiting**(擋單一 client 灌爆縮圖/推論)。
- 維持 SQLite + WAL(寫已 admin 收斂)。
- **這步就能撐「家庭 / 小團隊 NAS、少量並發瀏覽」。**

**Phase B — 推論獨立**
- tagging 拆**無狀態 sidecar**(鐵則 7);GPU box 可與 API box 分離。
- **觸發**:tagging backlog 餓死 API,或要上 Phase 2 CLIP。

**Phase C — 遷 Postgres + pgvector**
- **觸發**:多 admin 並行策展寫入互卡、讀負載要讀複本、或語意搜尋 vector 規模超過 sqlite-vec 舒適區。
- **一次拿到**:真並發寫、pgvector 語意搜尋、read replica。
- **遷移以 `file_hash` 為合併鍵**;integer surrogate id remap(見 §0)。

---

## 4. 不變的決策(即使走 NAS)

- **鐵則全數保留**:原檔唯讀 / 不寫回 metadata、`file_hash` 是身分、SQLite(或日後 Postgres)是 tag 唯一真相、軟刪、tag `source` 分流、ONNX EP 經 `IInferenceSessionFactory` 抽象、tagging 不直連 DB(無狀態 sidecar)、bind 離 localhost 即強制認證、路徑→tag 匯入後確認、**FK cascade 永遠開**。
- **integer PK 維持**(理由見 §0)。
- 縮圖永遠是**衍生快取**,絕不碰原圖。

---

## 5. 速查表

| 瓶頸 | admin-gating 後 | 主要解法 | 對應 Phase |
|---|---|---|---|
| B1 SQLite 單寫入 | 大幅降溫(政策單寫) | WAL(已)+ busy_timeout(已);長大遷 Postgres | C |
| B2 in-proc 推論搶資源 | 背景佇列、不在請求路徑 | tagging sidecar;CLIP 走 sidecar + vector index | B |
| B3 縮圖 thundering herd | 仍在(公開讀路徑) | per-hash single-flight + 靜態快取 + 預產 | A |
| B4 布林查詢讀負載 | 仍在(核心讀路徑) | 閉包快取 + 覆蓋索引 + facet 快取;讀複本 | A→C |
| B5 掃描 vs 服務 | admin 排程離峰 | 分批 + 降 IO 優先;移出主程序 | A→B |

**一句話總結**:NAS 版只要**有帳號系統把策展寫入收到 admin**,「寫」就不痛;真正要投資的是 **B3 縮圖、B4 讀查詢** 的讀/算擴展,推論與儲存則按 B、C 的觸發條件再分階段拆出去。
