# Pm.Ml 推論層架構盤點 — 為 CLIP / GPU 自動偵測鋪路

- 日期:2026-06-23
- 狀態:**評估報告(非待辦清單)**。給未來要動 GPU 自動偵測、CLIP 語意搜尋(Phase 2)、或第二個推論模型的人看,決定「哪些現在抽、哪些等真實形狀出來再抽」。
- 關聯:`CLAUDE.md` 鐵則 #6(ONNX/`IInferenceSessionFactory` 抽象、預設 DirectML、不硬綁 CUDA);`2026-06-23-scanner-tagging-refactor-design.md` §C(`Inference:Wd14:*` / `Inference:Clip:*` 能力開關已拆,Slice 4)。
- **核心結論:Pm.Ml 對 WD14 是乾淨、稱職的,現階段不需要為了「整理」而整理。** 只有一個小抽取對未來有實在價值,其餘多屬 YAGNI —— 等 CUDA/WinML/CLIP 的真實 I/O 形狀出來,正確的抽象自然會浮現。

## 1. 現況盤點(`src/Pm.Ml`,14 檔)

| 分類 | 檔案 | 性質 |
|---|---|---|
| **共用契約** | `IInferenceSessionFactory`(`Backend` + `Create(modelPath)`) | backend 無關,**已是正確共用點** |
| **backend** | `InferenceBackend`(enum)、`InferenceBackendSelector`(`Select(configured, gpuVendor)`)、`CpuSessionFactory`、`DirectMlSessionFactory`、`WindowsMlSessionFactory`(骨架,`Create` 丟 `NotSupportedException`) | 加 backend = `Wd14Setup.Available[]` 加一筆 |
| **WD14 契約/orchestrator** | `IWd14Tagger`(`TagAsync→(name,kind,conf)[]`)、`Wd14Tagger`(lazy session + semaphore gate) | tagging 專屬 |
| **WD14 pipeline** | `Wd14Preprocess`(448 / BGR / NHWC)、`Wd14Postprocess`(danbooru category + 門檻)、`Wd14Tags`(CSV parse)、`Wd14Tag`(record)、`Wd14ModelProvider`(HF 下載 + 原子 `.part`+rename) | 多為 WD14 專屬;**`DownloadAsync` 例外,見 §3** |
| **設定** | `Wd14Options`(ModelDir/URL/門檻/Size) | WD14 專屬 |
| **API 整合** | `Wd14Setup`(DI gate + factory 選擇)、`TaggingWorker`(消化 `tagging_job`) | — |

## 2. 共享得對的部分

- **`IInferenceSessionFactory`**:backend 抽象,CPU/DirectML 各自實作,WinML 骨架已保留介面形狀。CLIP 也能直接用同一支(推論 session 與模型語意無關)。
- **`Wd14Setup.Available[]` factory 註冊表**:加 backend 只需在陣列加一筆,被選到不存在的 backend 會明確報錯而非默默退化。擴充性好。

## 3. 唯一值得「現在(若 CLIP 近期)」抽的

`Wd14ModelProvider.DownloadAsync`(原子 `.part` + rename 下載)**完全不是 WD14 專屬** —— CLIP 也要下載模型檔。

- 抽成 `ModelArtifactDownloader.DownloadAsync(openStream, dest, ct)`(~20 行純重構),`Wd14ModelProvider` 與未來 `ClipModelProvider` 都呼叫它。
- 風險:零(純抽取,行為不變,可 TDD)。
- **但仍只在 CLIP 排上近期時才做**,否則同樣是預先優化。

## 4. 不要現在拉的(會拉錯 / YAGNI)

1. **inference factory base class**:CPU vs DirectML 只差 `new SessionOptions()` + append EP 那 ~15 行;而 `WindowsMlSessionFactory` 需要 **async 暖機 + 不同 ORT 來源**,會打破 template method。等 CUDA / WinML 真的落地、看清真正變異點再抽,否則賭錯。
2. **「模型」統一 base/interface**:WD14 出 tag(`(name,kind,conf)[]`),CLIP 出向量(`float[]`),**輸出與後處理根本不同**。硬拉「模型基類」是錯的抽象。正解是**平行兄弟**(見 §5)。
3. **泛型 `IImagePreprocessor` / `IPostprocessor<T>`**:WD14 是 448/BGR/NHWC + danbooru 門檻;CLIP 是 ~224/RGB/CHW + L2 normalize。差太多,等 CLIP 真實 ONNX I/O 形狀出來再決定抽不抽。

## 5. 模型怎麼擴(平行兄弟,不繼承統一基類)

WD14 與 CLIP 只共享兩個真正 cross-cutting 的東西:① `IInferenceSessionFactory`(已共享)② 模型下載 helper(§3 抽出)。其餘各自獨立:

```
共用:  IInferenceSessionFactory、ModelArtifactDownloader(抽出後)、InferenceBackendSelector
WD14:  IWd14Tagger / Wd14Tagger / Wd14Options / Wd14Preprocess / Wd14Postprocess / Wd14ModelProvider
CLIP:  IClipEmbedder / ClipEmbedder / ClipOptions / ClipPreprocess / ClipPostprocess / ClipModelProvider
設定:  Inference:Wd14:*  /  Inference:Clip:*   (Slice 4 已留位)
host:  AddWd14Tagging   /   AddClipEmbedding(平行擴充方法)
```

CLIP 落地 ≈ 一組平行的 `Clip*` 檔 + 復用 session factory + 抽出的下載 helper。**無架構阻礙**;設定層的 `Inference:Clip:*` 位 Slice 4 已備。

## 6. GPU 自動偵測 —— seam 已存在,可隨時做

`InferenceBackendSelector.Select(configured, gpuVendor)` **本就是為自動偵測設計的**,只是 `Wd14Setup.cs` 目前硬塞 `gpuVendor: null`。

- 要做:加 `IGpuVendorDetector` + Windows 實作(WMI 查 `Win32_VideoController` vendor,或 DXGI 列舉 adapter),啟動時同步偵測一次傳進 `Select`。失敗時 catch → 退 null → CPU。
- 好測:`SelectorTests` 已用 mock 的 `gpuVendor` 覆蓋分支邏輯;只需替偵測器本身補測。
- **價值評估**:對「無 GPU → 自動退 CPU」與「零設定」最有價值;對**已知雙 GPU、各機 launchSettings 設一次**的本專案場景,payoff 中等。**建議當獨立小 slice,想要再做,不急。**

## 7. 擴充性總表

| 未來功能 | 支撐度 | 要做什麼 |
|---|---|---|
| 加 backend(CUDA / WinML) | ✅ 好 | factory 進 `Available[]`;WinML 骨架已留介面 |
| GPU 自動偵測 | ✅ seam 已備 | 實作 `IGpuVendorDetector`(§6) |
| CLIP / 第二種模型類型 | 🟡 可,無阻礙 | 平行 `Clip*` 一組檔 + 復用 session factory + 抽出下載 helper;設定位已備 |
| 第二個 tagging 模型(同形狀) | ✅ 易 | 多半復用 `Wd14Preprocess/Postprocess`(參數化),新 Options/Provider |

**組織建議**:等**第二個模型真的落地時**,再把 `Pm.Ml` 輕分成 `Inference/`(backend 共用)、`Wd14/`、`Clip/` 三個子資料夾 —— 那時才知道邊界在哪。現在分是空殼。

## 8. 行動順序建議

1. **現在(僅當 CLIP 近期)**:抽 `ModelArtifactDownloader`(20 行,零風險,TDD)。
2. **想要時(獨立小 slice)**:GPU 自動偵測 `IGpuVendorDetector`(WMI)。seam 已備。
3. **延後(YAGNI,等觸發)**:factory base class(等 CUDA/WinML 顯露真實變異)、泛型 preprocess/postprocess(等 CLIP 真實形狀)、模型介面統一(別做 —— 維持平行兄弟)。
