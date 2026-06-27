# Pm.Ml 推論層架構盤點 — 為 CLIP / GPU 自動偵測鋪路

- 日期:2026-06-23
- 狀態:**評估報告(非待辦清單)**。給未來要動 GPU 自動偵測、CLIP 語意搜尋(Phase 2)、或第二個推論模型的人看,決定「哪些現在抽、哪些等真實形狀出來再抽」。
- **2026-06-27 增補**:§6 GPU 廠牌偵測**決議不做(moot)**;§9 釐清「EP 部署模型 vs runtime `Backend` 選擇」—— 為何有 `Backend` knob、enum 聯集 vs `Available[]`、經典自包含 vs Windows ML。
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

## 3. 唯一值得抽的 —— ✅ 已完成(2026-06-27)

`Wd14ModelProvider.DownloadAsync`(原子 `.part` + rename 下載)**完全不是 WD14 專屬** —— CLIP 也要下載模型檔。

- ✅ 已抽成 `ModelArtifactDownloader.DownloadAsync(openStream, dest, ct)`(純重構、行為不變),`Wd14ModelProvider.EnsureAsync` 改呼叫它;未來 `ClipModelProvider` 同樣復用。
- 原本已泛用命名 + 泛用測過(測 `.part`/atomic rename/失敗清檔,不碰 WD14),故只是搬到正確命名的家:測試一併移到 `ModelArtifactDownloaderTests`,非新增抽象。
- 風險:零(全測綠)。§4 其餘整理(base class / 泛型 preprocess / 統一介面)仍 **defer 到 CLIP 真實形狀出來**再評估。

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

## 6. GPU 自動偵測 —— ❌ 決議不做(moot,2026-06-27 複審)

**結論先行:此項刻意不做。** 兩個事實讓它失去消費者:

1. **廠商於 publish 時綁定** —— EP 由出貨套件決定(DirectML build 跨 NV/AMD/Intel;CUDA 走專屬 publish profile),runtime 偵測「是哪家顯卡」沒人要用;連未來 Windows ML 也是 ORT 自己用 device policy 挑(`SetEpSelectionPolicy`),不靠我們偵測廠商。
2. **Backend 一律明示** —— `appsettings.json` 出貨帶 `"Backend":"directml"`、各機 launchSettings 再覆寫,`Select` 永遠走 configured 短路,那條 `gpuVendor` auto 分支**實際從未執行**(`gpuVendor:null` 是裝飾品)。

唯一殘值是「無 GPU 機器避免 DirectML 退 WARP 軟體光柵的龜速」,但需先把 Backend 預設改成 auto 才生效;對**有顯卡且本就明示 Backend**的單人 app payoff 過低。故:`gpuVendor` 參數與 auto 分支**保留作 seam(harmless),但不實作偵測器**;`SelectorTests` 對該分支的覆蓋亦留著作行為文件。

以下為原評估,保留作背景(若日後 config 模型改成「auto 為預設」可回頭參考):

`InferenceBackendSelector.Select(configured, gpuVendor)` 本就是為自動偵測設計的,只是 `Wd14Setup.cs` 傳 `gpuVendor: null`。原規劃:加 `IGpuVendorDetector` + Windows 實作(WMI 查 `Win32_VideoController` vendor,或 DXGI 列舉 adapter),啟動偵測一次傳進 `Select`,失敗 catch → null → CPU。但如上,現有 config 模型下無觸發點。

## 7. 擴充性總表

| 未來功能 | 支撐度 | 要做什麼 |
|---|---|---|
| 加 backend(CUDA / WinML) | ✅ 好 | factory 進 `Available[]`;WinML 骨架已留介面 |
| GPU 自動偵測 | ❌ 不做(moot,見 §6) | 廠商 publish 綁定 + Backend 一律明示,auto 分支從未觸發、偵測無消費者;seam 保留不實作 |
| CLIP / 第二種模型類型 | 🟡 可,無阻礙 | 平行 `Clip*` 一組檔 + 復用 session factory + 抽出下載 helper;設定位已備 |
| 第二個 tagging 模型(同形狀) | ✅ 易 | 多半復用 `Wd14Preprocess/Postprocess`(參數化),新 Options/Provider |

**組織建議**:等**第二個模型真的落地時**,再把 `Pm.Ml` 輕分成 `Inference/`(backend 共用)、`Wd14/`、`Clip/` 三個子資料夾 —— 那時才知道邊界在哪。現在分是空殼。

## 8. 行動順序建議

1. ~~**現在(僅當 CLIP 近期)**:抽 `ModelArtifactDownloader`~~ ✅ **已完成(2026-06-27)**(純重構、全測綠,見 §3)。
2. ~~**想要時(獨立小 slice)**:GPU 自動偵測 `IGpuVendorDetector`~~ ❌ **不做(moot,2026-06-27,見 §6)**:廠商 publish 時綁定、Backend 一律明示,auto 分支從未觸發、偵測無消費者。
3. **延後(YAGNI,等觸發)**:factory base class(等 CUDA/WinML 顯露真實變異)、泛型 preprocess/postprocess(等 CLIP 真實形狀)、模型介面統一(別做 —— 維持平行兄弟)。

## 9. EP 部署模型 vs runtime `Backend` 選擇 —— 為何有這個 knob(2026-06-27 增補)

> 釐清一個反覆會被問的問題:「DirectML / Windows ML / CUDA / ROCm 各自是獨立部署的 DLL,那『裝完還能 runtime 選用誰』不就很不合理?發貨只發特定 DLL,哪來別家的 DLL?」這個直覺**對一半**,以下把對的部分與 nuance 拆清楚,當作 §6「不做廠商偵測」決議的支撐。

### 9.1 跨廠商 = publish 時就定死(直覺正確的部分)

每個 ORT 套件 = **一顆特定 build 的 `onnxruntime.dll`** + 它要的額外 native:

| 套件(publish flavor) | 帶的 native | runtime 可選 EP |
|---|---|---|
| `Microsoft.ML.OnnxRuntime`(CPU) | `onnxruntime.dll`(CPU build) | CPU |
| `Microsoft.ML.OnnxRuntime.DirectML`(**本專案**) | `onnxruntime.dll`(DML build)+ `DirectML.dll` | **CPU + DirectML** |
| `Microsoft.ML.OnnxRuntime.Gpu`(CUDA) | `onnxruntime.dll`(CUDA build),機器另需 CUDA/cuDNN | CPU + CUDA + TensorRT |
| ROCm / MIGraphX | AMD repo,Linux,非公開 NuGet | CPU + MIGraphX(ROCm EP 已於 ORT 1.23 移除) |

**DML build 與 CUDA build 是兩顆不同的 `onnxruntime.dll`、檔名相同、不能並存**(NuGet 直接衝突)。所以「裝完還能在 NV/AMD/CUDA/ROCm 之間切」在**經典自包含(single-file exe)模型下確實不合理** —— 發哪顆就只有哪顆,別家的 EP DLL 不在包裡。

### 9.2 但一顆 build 裡**不只一個 EP**(runtime knob 的正當理由)

CPU EP 是**永遠內建的 fallback**。故 DirectML build 實際帶 **{CPU, DirectML} 兩個** EP,runtime `Inference:Wd14:Backend` 是在「**這顆 build 真的帶的 EP 集合之內**」挑,不是假的。唯一實際用途:

> DML 驅動出包 / headless / 跑出問題時,設 `Backend=cpu` 強制退 CPU,**不必重發貨**。

這就是 `Backend` knob 還活著的理由;**跨廠商**那層則永遠是 publish 時的事,不是 runtime。

### 9.3 程式碼如何精準表達這件事

- `InferenceBackend` enum(`Cpu/DirectMl/Cuda/WindowsML`)= **所有 flavor 的聯集**(純語彙)。
- `Wd14Setup.Available[]` = `[Cpu, DirectMl]` = **這顆 build 真的帶的**。
- 在 DirectML build 設 `Backend=cuda` → `Wd14Setup.FactoryFor` 丟 `NotSupportedException`(「本 build 僅 cpu/directml;CUDA 需專屬 publish profile」)。

→ **「選一個沒發的 DLL」現在就會大聲報錯,不會默默退化。** 鐵則 6「要 NV 全速才另加 CUDA publish profile,**程式碼不動**」就是這機制:同份 code、換 publish profile 換那顆 `onnxruntime.dll`,enum 不變、`Available[]` 換一組。

### 9.4 唯一讓「裝完跨廠商選 / 自動挑」變合理的世界:Windows ML

WinML 是**另一種部署模型**:ORT 由**作業系統共享**,EP 是 **runtime 下載 / 註冊的 plugin(plugin-EP libraries)**,**不烤進部署包**。屆時別家 EP 才可能 runtime 才出現,「跨裝置自動挑 EP」(ORT `SetEpSelectionPolicy`,1.22+)才談得上 —— 且由 **ORT 自己挑,不是我們偵測廠商**。這正是本 codebase 把 WinML 當「另一個 publish flavor / Phase 2」、auto-detect 不選它的原因。

### 9.5 收束

| 世界 | 跨廠商在哪定 | runtime 還能做什麼 | 「偵測廠商」有無消費者 |
|---|---|---|---|
| **經典自包含**(現況) | publish 時(選哪顆套件) | 只在包內 EP 集合挑(CPU↔DML 退路) | **無** —— 靠 publish + `Available[]` + 選不到就報錯 |
| **Windows ML**(Phase 2) | runtime(EP 是 OS 外掛) | ORT 依 device policy 自動挑 | **無** —— ORT 自己挑,不靠我們 |

兩個世界**都沒有**「runtime 偵測是哪家顯卡」的消費者 —— 這就是 §6 決議不做 GPU 廠牌偵測的根本依據。

來源:[ORT Execution Providers](https://onnxruntime.ai/docs/execution-providers/)、[Plugin EP libraries(WinML 模型)](https://onnxruntime.ai/docs/execution-providers/plugin-ep-libraries.html)、[WinML select execution providers / device policy](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/select-execution-providers)。
