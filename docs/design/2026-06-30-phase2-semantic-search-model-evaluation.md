---
status: active
last-reviewed: 2026-06-30
supersedes: []
superseded-by: []
related: [2026-06-23-ml-layer-architecture-assessment, 2026-06-21-picture-management-design, 2026-06-25-second-tagger-cl-tagger-evaluation]
---

# Phase 2 語意搜尋 — 模型選型評估

- 日期:2026-06-30
- 狀態:**評估報告(非待辦、非落地設計)**。給日後要動 Phase 2 語意搜尋的人,先把「機制 / 能補什麼 / 模型怎麼選」攤清楚,作為後續落地設計(`Clip*` 平行檔 + 向量表 + 查詢端 API)的前置。**計畫先行**:本檔只做分析與建議,動 code 前另出落地設計再經確認。
- 關聯:主設計 [`2026-06-21-picture-management-design.md`](2026-06-21-picture-management-design.md) §Phase 2 與 §4 DDL(向量表草稿);[`2026-06-23-ml-layer-architecture-assessment.md`](2026-06-23-ml-layer-architecture-assessment.md)(已確認 CLIP 可復用 `IInferenceSessionFactory` + `ModelArtifactDownloader`,平行 `Clip*` 一組檔即可,**無架構阻礙**);`AGENTS.md` 鐵則 #6(ONNX in-proc、預設 DirectML、不硬綁 CUDA)、#7(ML 不另開程序;真要 Python 以無狀態 sidecar 接)。
- **核心結論(先講):** 對本專案三條硬約束(**① in-proc ONNX、② 單機單人要輕、③ 繁中查詢**)做篩選後,**務實落點是 `jina-clip-v2` 單編碼器**(輕、官方 ONNX、多語含繁中);**`DINOv2` 列為「以圖找圖」的可選增強**。新世代 VLM-based embedding(GME-Qwen2-VL / Qwen3-VL-Embedding)中文最強但 2B–7B 太重,塞進單機 in-proc 不切實際 —— 用上它等於被迫開 Python sidecar,與鐵則 #7 衝突,故**列為重量級升級選項而非起手式**。

---

## 1. 目的與範圍

- **要回答的問題**:Phase 2 的「CLIP image embedding → 向量查詢」具體用哪顆模型?繁中查詢怎麼成立?動漫域怎麼跟 WD14 分工?
- **非目標**:向量索引落地細節(sqlite-vec vec0 schema / HNSW 參數 / 查詢 API 形狀)留給後續落地設計;本檔只到「選哪顆、為什麼、對資料層有何影響」。
- **不改鐵則**:推論一律 in-proc ONNX 經 `IInferenceSessionFactory`;原圖唯讀;SQLite 為唯一真相。

## 2. 語意搜尋是什麼 / 能補什麼

核心 = **embedding(向量化)+ 最近鄰檢索**,跟 WD14 標籤是兩條平行線:

```
建索引(離線,掃描時跑一次)
  每張圖 ──> image encoder ──> 一條向量 float[D] ──> 存進向量表(sqlite-vec / pgvector)

查詢(線上)
  ┌ 文字查詢:「夕陽下的紅髮女孩」──> text encoder ──> 查詢向量
  └ 以圖找圖:某張圖的向量 ──────────────────────────> 查詢向量
                              ↓
        向量表算 cosine top-K ──> 結構化過濾(tag / 資料夾)後排序
```

相對現有能力的增量:

| 場景 | 現在(布林 tag) | 語意搜尋補上 |
|---|---|---|
| 標不出來的東西 | 找不到 | 「賽博龐克霓虹街景」「憂鬱雨天」直接打字 |
| 以圖找圖 | 無 | 點一張 → 找構圖/畫風/色調相似 |
| 近似去重 / 找變體 | 只能 hash(完全相同) | 找「幾乎一樣」(改圖、不同解析度、相似草稿) |
| 模糊語意排序 | tag 非此即彼 | 「比較像 A 而非 B」的相似度排序 |

**定位**:語意搜尋**不取代** WD14。WD14 出離散標籤(角色名、作品名,精確命名),語意搜尋出連續向量(氛圍/構圖/場景,標不出標籤的東西)。兩者互補。

## 3. 關鍵分水嶺 —— 圖像編碼器有兩種血統

選型最該先懂的一條:**圖像編碼器分兩血統,用途不同,別混為一談。**

| 血統 | 代表 | 有文字端? | 最強場景 | 對 in-proc 友善度 |
|---|---|---|---|---|
| **CLIP 系**(圖文對齊) | SigLIP 2 / jina-clip-v2 / Chinese-CLIP | ✅ | **文字→找圖**(需圖文同空間) | 中(看模型大小) |
| **自監督純視覺** | DINOv2 | ❌ | **以圖找圖**(純畫面相似、與語言無關) | 高(小、快、ONNX 友善) |

- 要**用繁中打字找圖** → 必走 CLIP 系(吃文字端)。
- **只要以圖找相似** → DINOv2 反而更準,且沒有文字端、體積小、最適合 in-proc;代價是**不能單獨用文字查**。

> 截至 2026-06,SigLIP 2 是公認最強的開源「文字↔圖」相似模型;DINOv2 在「純以圖找圖」上勝出。兩者是不同任務的冠軍,不是同一條跑道。

## 4. 模型候選盤點

世代已位移:2025→2026 新主力是 **VLM-based embedding**(拿 Qwen2-VL/Qwen3-VL 微調),中文原生強,但**很大**。

| 模型 | 出處 / 時間 | 中文 | 規模 | 文字端 | 官方 ONNX | 備註 |
|---|---|---|---|---|---|---|
| **GME-Qwen2-VL-2B / 7B** | 阿里 Alibaba-NLP,2025 | ✅ 原生強 | 2B / 7B | ✅ | 🟡 難 | Any2Any(文字/圖/圖文對),中文檢索頂;**太重,in-proc 不切實際** |
| **Qwen3-VL-Embedding / Reranker** | 阿里,2026-01 | ✅ | 大 | ✅ | 🟡 | 最新統一多模態 embedding,同樣偏重 |
| **BGE-VL** | BAAI,2025 | ✅ | 中大 | ✅ | 🟡 | 阿里以外的中文多模態選擇 |
| **jina-clip-v2** | Jina,2024-12 | ✅ 多語含繁中 | **0.9B** | ✅ | **✅** | 89 語、512×512、Matryoshka(1024→64);**最貼 in-proc 約束** |
| **SigLIP 2** | Google,2025-02 | ✅ 多語 | 中(ViT-L 等) | ✅ | 🟡 可轉較費工 | 文字↔圖品質頂;動漫域非專門 |
| **Chinese-CLIP** | OFA-Sys,老牌 | 🟡 **簡體訓練** | 輕 | ✅ | ✅ | 最輕;繁中要先 OpenCC 繁→簡 |
| **DINOv2** | Meta | —(無文字) | 小 | ❌ | ✅ | 純視覺以圖找圖最準、最 ONNX 友善 |

## 5. 用三條硬約束篩選(決策核心)

| 候選 | ① in-proc ONNX | ② 單機輕量 | ③ 繁中 | 結論 |
|---|---|---|---|---|
| GME / Qwen3-VL / BGE-VL | ✗(難轉、重) | ✗(2B–7B) | ✅ | **被迫 sidecar**,違鐵則 #7;列重量級升級選項 |
| **jina-clip-v2** | ✅ 官方 ONNX | ✅ 0.9B | ✅ 多語 | **首選**,三項全過 |
| SigLIP 2 | 🟡 可轉費工 | 🟡 | ✅ | 文字↔圖更強但轉檔成本高,列備案 |
| Chinese-CLIP | ✅ | ✅ | 🟡 簡體 | 需 OpenCC;繁中表現不如多語原生 |
| **DINOv2** | ✅ | ✅ | —(無文字) | **以圖找圖**的可選增強 |

**Matryoshka 的實務價值**:jina-clip-v2 可把向量從 1024 維截到 64 維再存。十萬量級下,維度直接乘上儲存與檢索成本,這個可調點對單機很實在(先用較低維上線,不夠再加)。

## 6. 繁中陷阱(最易被忽略)

⚠️ **CLIP 的「文字端」與「圖像端」是兩回事。** 以圖找圖只用圖像端,與語言無關;但**用繁中打字找圖吃文字端**,而經典 OpenAI CLIP 文字端只懂英文 → 直接打中文幾乎無效。所以選型真正的變數是「文字端支不支援繁中」。

- **多語原生模型**(jina-clip-v2 / SigLIP 2):繁中較自然,直接可用。
- **簡體訓練模型**(Chinese-CLIP):查詢端要掛一層 **OpenCC 繁→簡**,否則繁中專有寫法掉分。這是最容易漏的掉分點。

## 7. 動漫域的取捨

動漫專用 CLIP 幾乎都是**英/日文字端**,跟「繁中查詢」直接衝突。故折衷:

- **動漫的精確命名(角色 / 作品)繼續交給 WD14 tag**(已上線、精度高)。
- **語意搜尋專攻 WD14 標不出的「場景 / 氛圍 / 構圖」**,用通用多語編碼器即可。
- 不為了「動漫域」去換成英/日專用 CLIP 而犧牲繁中查詢能力。

(呼應 [`2026-06-25-second-tagger-cl-tagger-evaluation.md`](2026-06-25-second-tagger-cl-tagger-evaluation.md):第二 tagger 走 deferred;語意搜尋是另一條互補軸,不是再加一個 tagger。)

## 8. 建議落點

考量本專案是**動漫圖庫 + 繁中查詢 + 單機 in-proc**:

1. **主推:`jina-clip-v2` 單編碼器**(文字↔圖 + 以圖找圖一套兼顧)。輕、官方 ONNX、繁中可用,符合全部鐵則。先用較低 Matryoshka 維度上線,品質不足再加維。
2. **可選增強:`DINOv2`**(雙編碼器分工)—— 若「以圖找圖」用 jina 不夠準,再加 DINOv2 專做純視覺相似。代價:兩套向量、兩倍儲存與兩次推論。**非起手式,留作補強。**
3. **重量級升級選項:`GME-Qwen2-VL`** —— 若日後真要極致中文檢索、且願意接受開 Python sidecar(無狀態、POST 回 API、不直連 SQLite,鐵則 #7),再評估。**不在 Phase 2 起手範圍。**

> 一句話:**先 jina-clip-v2 一套打天下;DINOv2 補「以圖找圖」;GME 等到願意付 sidecar 代價時再說。**

## 9. 對資料層 / 設定層的影響(供落地設計接手)

- **向量維度非固定 768**:主設計 §4 DDL 的 `vector(768)` 是占位。實際維度依選定模型(jina-clip-v2 原生 1024,可 Matryoshka 截短;DINOv2 視 backbone 而定)。落地設計需把維度作為**模型決定的參數**,別寫死。
- **儲存引擎**:Phase 2 走 **sqlite-vec(vec0 虛擬表)**;若屆時 sqlite-vec 仍 alpha / 不夠用,則一次性遷 **Postgres+pgvector**(`vector(D)` + HNSW)。EF 抽象已在,搬移以資料為主。
- **推論接縫已備**:CLIP 復用 `IInferenceSessionFactory`(session 與模型語意無關)+ 已抽出的 `ModelArtifactDownloader`;落地 ≈ 一組平行 `Clip*` 檔(`IClipEmbedder` / `ClipEmbedder` / `ClipOptions` / `ClipPreprocess` / `ClipPostprocess` / `ClipModelProvider`)。**無架構阻礙。**
- **設定位已備**:能力層開關 `Inference:Clip:*`(Slice 4 已拆,改了要重啟);與 WD14 同走 `appsettings` / launchSettings。
- **前處理差異**(供 preprocess 抽象決策):WD14 是 448 / BGR / NHWC + danbooru 門檻;CLIP 系約 224–512 / RGB / CHW + L2 normalize;DINOv2 另有自己的 normalize。差異大,維持**平行兄弟**、不硬拉共用基類(沿用 ml 盤點 §4 結論)。

## 10. 開放問題(待落地設計拍板)

1. 單編碼器(jina)還是雙編碼器(jina + DINOv2)起手?取決於「以圖找圖」是否為 Phase 2 一級需求。
2. Matryoshka 起始維度取多少(64 / 256 / 512 / 1024)?需用真實圖庫抽樣量測「召回 vs 儲存」拐點。
3. sqlite-vec 屆時成熟度?若仍 alpha,是否直接上 pgvector(牽動「單機雙擊即開、零常駐」的取捨)。
4. jina-clip-v2 ONNX 的實際 I/O 形狀 / 授權條款 / 模型體積,落筆前用 Context7 撈官方最新文件確認。

## 11. 參考來源

- jina-clip-v2:Multilingual Multimodal Embeddings(arXiv 2412.08802)；`jinaai/jina-clip-v2`(HF)。
- GME-Qwen2-VL(`Alibaba-NLP/gme-Qwen2-VL-2B-Instruct`,HF / ModelScope);Qwen3-VL-Embedding & Reranker(arXiv 2601.04720);BGE-VL(BAAI,2025)。
- SigLIP 2:Multilingual Vision-Language Encoders(arXiv 2502.14786)。
- SigLIP 2 vs DINOv2 對比(Underfitted.dev,2026-03);多模態 embedding 2026 評測(Spheron / benchmark)。

> 模型版本與 ONNX/授權細節變動快;進入落地設計前以 Context7 / 官方頁覆核當下事實,本檔結論以「選型邏輯與約束」為準,具體型號可隨當時最佳實作微調。
