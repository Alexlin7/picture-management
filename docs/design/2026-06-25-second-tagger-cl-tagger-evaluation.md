---
status: active
last-reviewed: 2026-06-29
supersedes: []
superseded-by: []
related: [2026-06-23-ml-layer-architecture-assessment]
---

# 第二 tagger(cl_tagger_v2)評估 — ITagger 抽象

- 日期:2026-06-25
- 狀態:**評估記錄 · 低優先 deferred(未排程)**。只是「多一個 tagger 選擇可做開關」的可能性,沒有一定要做。
- 來源:朋友提供 `cella110n/cl_tagger_v2`(https://huggingface.co/cella110n/cl_tagger_v2)。
- 結論先講:**技術可行,但不是「抽換」WD14,是「新增一種 tagger(開關)」的中等重構(估 2–3 天)。動工前先確認授權/速度/品質三件事(見 §四)。**

## 一、cl_tagger_v2 事實

| 項目 | 值 |
|---|---|
| backbone | Google SigLIP2 SoViT-400m/14 + LoRA(rank 32) |
| 格式 | **ONNX**(`model.onnx` + `model.onnx.data` 外部權重)、`model_vocabulary.json`、`model_metadata.json` 等 |
| 輸入 | **384×384、RGB、NCHW `[b,3,384,384]`、正規化 (x−0.5)/0.5** |
| 輸出 | **logits → 需自己 sigmoid**;**108,036 tags** |
| tag 對應 | `model_vocabulary.json`(JSON:`idx_to_tag` + `tag_to_category`) |
| tag 體系 | **Danbooru 系**:Character/General/Copyright/Meta/Rating/Quality |
| 依賴 | ONNX Runtime(CUDA/CPU) |
| 授權 | **自訂「CL Tagger v2 License v1.0」:禁止再配布、禁止無償以外有償提供**(基於 SigLIP2 Apache 2.0 訓練) |

## 二、現有 WD14 架構抽象到哪(對照)

- ✅ **推論後端已抽象**:`IInferenceSessionFactory`(CPU/DirectML/CUDA/WindowsML)→ 任何 ONNX 模型都能跑,**cl_tagger 直接複用這層**。
- ✅ input/output tensor 名動態讀(`InputMetadata.Keys.First()` / `results.First()`);`Size` 可配置。
- ❌ **模型前/後處理與 tag 體系是 WD14 寫死**,沒有 `ITagger` 通用介面:
  - `Wd14Preprocess`:**448×448、BGR、NHWC、0–255 無正規化、白底 padding**(寫死)。
  - `Wd14Postprocess`:無 sigmoid(WD14 模型內建)、category 對應 `0/3/4/9` 寫死。
  - `Wd14Tags.Parse`:吃 `selected_tags.csv`(CSV id/name/category,寫死);檔名 `model.onnx`/`selected_tags.csv` 寫死。
  - DI:`AddSingleton<IWd14Tagger, Wd14Tagger>`;`TaggingWorker` 直接依 `IWd14Tagger`。

## 三、為什麼是「新增」不是「抽換」

`IInferenceSessionFactory` 抽象的是「在哪跑 ONNX」,不是「模型怎麼前後處理 / tag 體系」。cl_tagger 與 WD14 在**解析度(384 vs 448)、色彩(RGB vs BGR)、排列(NCHW vs NHWC)、正規化(有 vs 無)、sigmoid(要 vs 不要)、tag 格式(JSON vs CSV)** 全不同 → 等於要寫一整套新的 pre/post/loader。差異對照見上表。

要做成「開關」需:
1. 抽 `ITagger` 介面(回傳一致的 `(Name, Kind, Conf)`)+ `ITaggerPreprocess`/`ITaggerPostprocess`/tag-loader。
2. WD14 與 cl_tagger 各自實作(WD14 既有邏輯搬進實作、不改行為)。
3. `TaggingWorker` 改依 `ITagger`;`Inference:*` 設定選當前 tagger(沿用 opt-in 開關模式)。
4. cl_tagger adapter:384 RGB NCHW + (x−0.5)/0.5 前處理、sigmoid 後處理、`model_vocabulary.json` 解析、Quality category 對應。
- 影響檔案約 7–10 個(見探索報告);基礎(ONNX 後端抽象)已有,不從零。

## 四、動工前必先確認(否則不要開始)

1. **授權**:cl_tagger 禁止再配布。**app 不能像 WD14 那樣自動從 HF 下載並內建分發**。個人自用大概 OK;若 app 要給別人用 → 踩線。先釐清你的使用情境。
2. **速度**:SigLIP2 400m + 108k 輸出比 WD14 v3(~10k)重很多。在本機 DirectML(AMD RX 9060 XT)實測單張推論時間,確認可接受。
3. **品質**:跑幾張你真實圖庫的圖,對比 cl_tagger vs WD14 v3 的標籤品質,確認真的更好、值得這個重構。

三件事都過再走 `brainstorming → spec(ITagger 設計定稿)→ plan → 實作`。

## 五、固定決策(若日後做)

- WD14 既有行為**不改**(搬進 `ITagger` 實作時保持等價,有測試守門)。
- 新 tagger 走相同 opt-in 設定開關;不預設啟用。
- tag kind 體系沿用現有(character/copyright/general/meta);Quality 暫並入 general 或新增 kind(屆時定)。
- 自動下載僅對授權允許的模型;cl_tagger 因授權限制,改「使用者自備模型檔放 ModelDir」而非內建下載 URL。
