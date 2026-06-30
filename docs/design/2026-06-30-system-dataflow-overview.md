---
status: active
last-reviewed: 2026-06-30
supersedes: []
superseded-by: []
related: [2026-06-21-picture-management-design, 2026-06-30-phase2-semantic-search-model-evaluation, 2026-06-23-ml-layer-architecture-assessment]
---

# 系統資料流總覽(現況快照 + 語意搜尋層定位)

- 日期:2026-06-30
- 狀態:**導覽文件(現況快照)**。把整套系統「資料怎麼流」一頁攤開,給新接手者建立全貌,並標出 Phase 2 語意搜尋層會插在哪一格、補什麼。
- **真相源歸屬**:架構 / ER / DDL / 決策日誌的 canonical 是主設計 [`2026-06-21-picture-management-design.md`](2026-06-21-picture-management-design.md);本檔是**對齊程式碼現況的導覽**,衝突時以主設計與 `AGENTS.md` 鐵則為準。行號對應撰寫當下 working tree,程式演進後以程式碼為準。
- 關聯:語意搜尋層的選型理由見 [`2026-06-30-phase2-semantic-search-model-evaluation.md`](2026-06-30-phase2-semantic-search-model-evaluation.md);ML 推論層接縫見 [`2026-06-23-ml-layer-architecture-assessment.md`](2026-06-23-ml-layer-architecture-assessment.md)。

---

## 1. 系統全貌

整個 app 是**單一 .NET 程序**(Angular 靜態檔由 .NET serve、REST 只 bind `localhost`、嵌入式 SQLite、ONNX in-proc)。底下三條資料流並行:

- **流 ①:掃描 / 索引**:原圖 → 算身分(hash)→ 寫 `photo` / `photo_location` + EXIF + 縮圖 + 排標籤 job。
- **流 ②:WD14 標籤**:程序內 DB-backed 佇列 `tagging_job` → ONNX 推論 → 寫 `photo_tag`。
- **流 ③:查詢 / 看圖**:布林多軸 tag 查詢 + facet 樹 + 縮圖 / 原圖串流 → JSON 回前端。

三條流的唯一真相都是同一顆 SQLite 檔;原圖一律唯讀,衍生資料(縮圖)放 app 自有快取目錄。

---

## 2. 流 ①:掃描 / 索引(原圖 → SQLite + 縮圖快取)

入口 `POST /api/roots/{id}/scan` → `RootScanCoordinator`(背景 `Task`,單 root 一次一個,`/scan-status` 輪詢)→ `LibraryScanner.ScanRootAsync`。

```
POST /api/roots/{id}/scan
   │  (RootScanCoordinator:背景 Task,單 root 一次一個,/scan-status 輪詢)
   ▼
LibraryScanner.ScanRootAsync
   │
   ├─ 遞迴枚舉 root 下影像檔(副檔名白名單:png/jpg/jpeg/gif/webp/bmp/avif/heic/heif/jfif)
   │
   ├─ 快路徑:同位置 + size 同 + mtime 差<1s ──► 只更新 LastSeenAt(免重算 hash)
   │
   └─ 慢路徑(新檔 / 變更):
        ├─ Sha256FileHasher ─────► file_hash(唯讀開檔,絕不碰原圖)= 身分
        ├─ ExifImageMetadataReader ─► width/height/mime + 相機 / GPS / DateTimeOriginal + 全 EXIF→JSON
        ├─ ThumbnailService ───────► 512px 長邊 WebP,落 thumbs/{hh}/{hh}/{hash}.webp(依 hash,不碰原圖)
        │
        ▼ 一個 transaction 內:
        ├─ photo            ◄── 身分(hash 去重;同 hash 不重複建)
        ├─ photo_location   ◄── 位置(rootId + relPath + status=present)
        └─ tagging_job      ◄── 排一筆 pending(若 WD14 開啟、且可解碼)
        │
        └─ 對帳:這輪沒看到、仍 present 的 location ─► 軟刪標 missing(保留 photo+tags)
```

**身分 / 位置脫鉤**:`file_hash` 是身分、`file_path` 只是位置。搬檔 = 新路徑 hash 命中既有 photo → 建新 location;舊路徑沒被看到 → 標 missing。photo 身分完全不動 = 搬移偵測的本質。

**路徑→tag 是「匯入後確認」**,不在掃描內自動套:掃描只記錄 location;`PathTagService.GetPendingSegmentsAsync` 列出待確認路徑段供前端確認,`ApplyRuleAsync` 建 `path_tag_rule` 後才對含該段的 photo 寫 `photo_tag`(source=`path`)。

---

## 3. 流 ②:WD14 自動標籤(程序內 ONNX 佇列)

```
tagging_job(pending) ◄── 掃描時排入 / 或 TaggingScheduler 手動重排(retry/refresh/clear)
   │
   ▼
TaggingWorker(BackgroundService,單 worker;Inference:Wd14:Enabled=true 才註冊)
   │  撈一筆 pending → 標 running → 由 present location 組絕對路徑
   ▼
Wd14Tagger.TagAsync(singleton,session 重用)
   │  Preprocess(448px / BGR / NHWC) → InferenceSession.Run → Postprocess(門檻篩 + category→kind)
   │      └─ session 由 IInferenceSessionFactory 建(編譯期 flavor:預設 DirectML / CUDA / WinML / CPU)
   ▼
寫回:
   ├─ tag            ◄── UpsertByNameAsync(NameCi 全 Unicode 去重)
   ├─ photo_tag      ◄── source=wd14 + confidence
   └─ tag_relation   ◄── character 命中時,SeedFromCharacter 拆「作品→角色」DAG 邊
   │
   └─ 成功標 done;例外 attempts++ 標 error(可重排)
```

- 啟動先 `RecoverStuckJobsAsync`:把上次崩潰卡 `running` 的 job 重設 `pending`(單 worker 前提)。
- Postprocess 門檻:category 4=character 用 `CharacterThreshold`(0.85)、其餘 `GeneralThreshold`(0.35)、rating 丟棄。
- **flavor 由編譯期決定**:永遠有 CPU,`#if INFER_DIRECTML/CUDA/WINDOWSML` 三選一(三套 native ORT 互斥);選到本 build 沒帶的 backend 會明確 `NotSupportedException`。

---

## 4. 流 ③:查詢 / 看圖(SQLite → JSON → 前端)

```
前端(Angular SPA,同源,由 .NET serve)
   │
   ├─ POST /api/search ──► PhotoQueryService.SearchAsync
   │      ├─ 每個 all-tag 展「後代閉包」群組(TagClosureService,WITH RECURSIVE 沿 tag_relation)
   │      ├─ AND of OR + none-tag 閉包排除
   │      ├─ 資料夾軸(rootId + pathPrefix 限子樹)
   │      ├─ 只算有 present location 的 photo
   │      └─ keyset 分頁(afterId + OrderBy Id desc,多取一筆判斷下一頁)──► JSON 分頁
   │
   ├─ GET /api/tags/tree ─► TagFacetService:用 tag_relation 組「作品→角色」facet 樹 + count
   │
   ├─ GET …/thumb ───────► 串縮圖 WebP(快取目錄)
   └─ GET …/file ────────► 後端代串原圖 bytes(瀏覽器沙盒開不了本機檔;API bind localhost 保證是本機)
```

布林多軸查詢的本質是 **AND of OR**:每個要包含的 tag 各自展成「自身 + 全後代」的閉包群組,照片需命中所有群組;排除 tag 的閉包做差集。facet count 只算「直接擁有」避免每節點跑 recursive CTE。

**REST 端點全貌**(`src/Pm.Api/Endpoints/`,皆 `Program.cs` 註冊):

| 端點檔 | 路由前綴 | 用途 |
|---|---|---|
| `HealthEndpoints` | `/health`, `/health/db` | liveness / readiness |
| `RootEndpoints` | `/api/roots`、`…/scan`、`…/scan-status` | 圖庫來源 CRUD + 觸發 / 查掃描 |
| `ReconcileEndpoints` | `/api/reconcile/missing` | 列出非 present 的 photo |
| `PathTagEndpoints` | `/api/roots/{id}/pending-segments`、`/api/path-rules`、`…/apply-path-tags` | 路徑段確認 / 建規則 / 套規則 |
| `SearchEndpoints` | `/api/search`、`/api/search/count` | 布林多軸查詢 + 計數 |
| `PhotoEndpoints` | `/api/photos/{id}` + `/thumb` `/file` `/archive` `/tags` `/reprocess` | 詳情 / 縮圖 / 原圖 / 軟刪 / 手動標籤 / 重處理 |
| `SavedSearchEndpoints` | `/api/saved-searches` | 儲存搜尋 CRUD |
| `BrowseEndpoints` | `/api/folder-roots`、`…/folder-tree`、`/api/browse/folder-tags` | 資料夾瀏覽維度樹 |
| `TagEndpoints` | `/api/tags/tree`、`/api/tags` + `merge` | facet 樹 + 標籤庫管理 / 合併 |
| `TaggingEndpoints` | `…/retag`、`/api/tag/requeue`、`/api/tagging/stats` | 重排 WD14 job + 佇列統計 |
| `MaintenanceEndpoints` | `/api/maintenance/orphan-photos`、`…/copyright-axis/rebuild` | 孤兒清理 + 作品軸重建 |

---

## 5. 資料模型(九表關聯)

```
library_root ─1:N─ photo_location ─N:1─ photo ─1:N─ photo_tag ─N:1─ tag
                       (位置層)          (身分層)                      │ 自參考
                    present/missing/    hash 唯一                       │
                       archived                                   tag_relation(DAG 邊:作品→角色)
                                          │
                                          └─1:0..1─ tagging_job(PK=PhotoId,一圖至多一 job)

path_tag_rule ─(可選)─► tag / library_root        saved_search(獨立,存 QueryJson)
```

- **身分 / 位置兩層拆開**是整個設計的核心:換碟、搬資料夾、同圖兩份,全是 `photo_location` 的增刪,`photo` 不動 → tag 與檔案系統徹底脫鉤。
- `photo_tag.source` ∈ path / manual / wd14(WD14 帶 `confidence`)→ 手動策展與自動標籤分得開。
- `tag.kind` ∈ path / manual / character / copyright / general / meta;`tag.name_ci`(全 Unicode 小寫鍵)+ 唯一索引做去重。
- FK cascade 全程由 SQLite 連線層強制開,硬刪靠 DB cascade,程式不逐表手刪(鐵則 #10)。

DDL 與設計重點的真相源在主設計 §4,本表只供導覽。

---

## 6. 語意搜尋層(Phase 2)定位 —— 插在哪、為什麼加

**現況:這層目前 0 行執行程式碼。** `src/Pm.Ml/` 下只有 WD14 與推論工廠,**沒有任何 `Clip*` 檔**;全 repo 只有兩處註解預留 seam(`Wd14Setup.cs` 的「未來 CLIP 走 `Inference:Clip:*`」、`ModelArtifactDownloader` 的「WD14 與未來 CLIP 共用」)。它只活在設計文件,屬 **Phase 2 規劃**。

### 6.1 插在資料流的哪一格

它是**平行於流 ② 的第二條 ML 流**,共用同一個 `IInferenceSessionFactory` + `ModelArtifactDownloader`,但落地到一張新的向量表:

```
建索引(離線,掃描時跑一次)
  photo ─► image encoder(CLIP,經同一個 IInferenceSessionFactory)─► float[D] ─► photo_vector
                                                                              └ sqlite-vec / pgvector

查詢(線上,在流 ③ 多一條「相似度」軸)
  「夕陽下的紅髮女孩」─► text encoder ─┐
  或 某張圖的向量 ──────────────────┤─► cosine top-K ─► 再走現有結構化過濾(tag / 資料夾)排序
                                      ▼
                              PhotoQueryService(布林 tag 之外,多一條相似度軸)
```

落地 ≈ 一組平行 `Clip*` 檔(`IClipEmbedder` / `ClipEmbedder` / `ClipOptions` / `ClipPreprocess` / `ClipPostprocess` / `ClipModelProvider`)+ 一張向量表 + 查詢端 API。ML 盤點已確認**無架構阻礙**。

### 6.2 「兩個編碼器」≠「兩顆模型」(最易誤會處)

上面的 `image encoder` 與 `text encoder` 是**同一顆模型的兩個塔(two-tower)**,不是兩套要分別嫁接的模型:

```
            ┌─ 影像塔 (image encoder) ─┐
jina-clip-v2 ┤                          ├─► 同一個向量空間 (float[D])
            └─ 文字塔 (text encoder) ──┘
```

- **要上語意搜尋,起手只嫁接「一顆」模型(jina-clip-v2)** —— 它自帶影像塔 + 文字塔,一套同時拿到「文字找圖 + 以圖找圖」。
- **文字之所以能找圖,關鍵在「同一個向量空間」**:兩塔在訓練時被對齊到同一座標系,所以圖餵影像塔、文字餵文字塔,出來的向量可以直接算距離。經典 OpenAI CLIP 文字塔只懂英文 → 繁中查詢吃的就是這個文字塔(選型評估的核心變數)。
- 工程上只下載 / 嫁接**一顆** ONNX,走既有 `IInferenceSessionFactory`;建索引用影像塔,查詢時看打字(文字塔)或點圖(影像塔)。

**只有**要再補純視覺的 `DINOv2`(可選增強)時,才會變成真的兩顆獨立模型:

| | jina-clip-v2 | DINOv2(可選增強) |
|---|---|---|
| 塔 | 影像 + 文字(雙塔) | 只有影像塔,**無文字端** |
| 能做 | 文字找圖 + 以圖找圖 | 只能以圖找圖(但更準) |
| 代價 | 一套向量 | 多一套向量、多一次推論、雙倍儲存 |

一句話:**起手 = 1 顆模型(自帶兩塔);「兩顆模型(jina + DINOv2)」是日後嫌以圖找圖不夠準才補的進階選項,非起手式。** 不管一顆兩顆,嫁接點都是同一個 `IInferenceSessionFactory`。

### 6.3 為什麼加(補現在做不到的事)

現有查詢是**布林 tag**——非此即彼、且只找得到「標得出標籤」的東西。語意搜尋補的是這四個缺口:

| 場景 | 現在(布林 tag) | 語意搜尋補上 |
|---|---|---|
| 標不出來的東西 | 找不到 | 「賽博龐克霓虹街景」「憂鬱雨天」直接打字 |
| 以圖找圖 | 完全沒有 | 點一張 → 找構圖 / 畫風 / 色調相似 |
| 近似去重 / 找變體 | 只能 hash(完全相同才算) | 找「幾乎一樣」(改圖、不同解析度、相似草稿) |
| 模糊排序 | tag 非此即彼 | 「比較像 A 而非 B」的相似度排序 |

**定位:不取代 WD14,而是互補。** WD14 出離散標籤(角色名、作品名,精確命名);語意搜尋出連續向量(氛圍、構圖、場景,標不出標籤的那一大片)。十萬量級動漫圖庫裡「我知道但說不出 tag」那堆,正是布林查詢的死角。

### 6.4 為什麼切成 Phase 2、而非 Phase 1 一起做

刻意延後,因為它有兩個 Phase 1 主幹不想背的重量:

1. **向量儲存**:sqlite-vec 仍 alpha,或得遷 Postgres+pgvector,牽動「單機雙擊即開、零常駐」的取捨。
2. **模型選型**:繁中查詢吃「文字端」,而經典 CLIP 文字端只懂英文 → 選型是真變數(選型結論見 [`2026-06-30-phase2-semantic-search-model-evaluation.md`](2026-06-30-phase2-semantic-search-model-evaluation.md):`jina-clip-v2` 單編碼器起手)。

Phase 1 先把「就地索引 + 布林查詢 + WD14」做穩、交付單檔 exe;語意搜尋作為加分軸獨立疊上去。主設計 §Phase 2 DDL 的 `vector(768)` 是占位,實際維度由選定模型決定。

---

## 7. 真相源與下一步

- 架構 / ER / DDL / 決策:主設計 [`2026-06-21-picture-management-design.md`](2026-06-21-picture-management-design.md)(canonical)。
- 鐵則與開發約定:根目錄 [`AGENTS.md`](../../AGENTS.md)。
- 語意搜尋選型:[`2026-06-30-phase2-semantic-search-model-evaluation.md`](2026-06-30-phase2-semantic-search-model-evaluation.md)。
- **未決**:Phase 2 落地設計(`Clip*` 平行檔 + 向量表 schema + 查詢 API)尚未動筆;計畫先行,動 code 前另出落地設計再經確認。
