using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

/// <summary>
/// Phase 2 待啟用 —— Windows ML 後端骨架(尚未實作,<see cref="Create"/> 會丟例外)。
///
/// 為什麼存在這個檔:把「未來可插上 Windows ML」的接縫先佔好,讓 enum / selector / DI 形狀穩定;
/// 上層 WD14 標籤服務只依賴 <see cref="IInferenceSessionFactory"/>,日後啟用時介面與呼叫端一行都不用動。
///
/// Windows ML 是什麼(實況,非宣傳):
///   - 本質是「OS 共享的 ONNX Runtime + 動態 EP 目錄管理層」,不是 ORT 的替代品 —— 同一套
///     <see cref="InferenceSession"/> 執行 API,差別只在「ORT 來源」與「EP 由誰管」。
///   - 由 Windows App SDK 1.8.1+ 提供;硬體 EP 目錄要求 Windows 11 24H2(build 26100)+。
///   - 動態取得 vendor EP(NVIDIA TensorRT-for-RTX、Intel OpenVINO、AMD Vitis AI/NPU、Qualcomm QNN),
///     app 不必自帶 vendor 原生庫;預設仍含 CPU / DirectML EP 作 fallback。
///   - NVIDIA RTX 30xx+ 走 TensorRT-RTX,較 DirectML 約快 2x(批次標籤十萬張時有感)。
///
/// 啟用前要解的事(刻意留到 Phase 2):
///   1. 套件與交付:Windows ML 的 ORT 來自 OS/App SDK,「不可」與自包管的
///      Microsoft.ML.OnnxRuntime.DirectML 在同一程序混用(兩套 native ORT 會打架)。
///      做法 = 另開一個 publish flavor(App SDK + Windows ML),與預設的「自包單檔 exe(DirectML)」分離。
///   2. 首次取得硬體 EP 需連網下載 —— 與本專案「離線雙擊即開」鐵則有張力,需在 UX/設定上處理。
///   3. EP 註冊是 async(例:ExecutionProviderCatalog.GetDefault().EnsureAndRegisterCertifiedAsync()),
///      故啟用時本工廠會需要一次性 InitializeAsync() 暖機;Create() 仍維持同步回傳 InferenceSession。
///   4. Selector 偵測:OS build ≥ 26100、GPU vendor、App SDK / EP 目錄可用性 → 失敗一律 fall back。
///
/// 何時才該真的啟用:主力機為 NVIDIA RTX 30xx+ 且 Win11 24H2+(要 2x)、或要支援 NPU。
/// 否則維持 ORT + DirectML 自包交付即可(AMD 獨顯在 Windows ML 底下仍走 DirectML,切了無感)。
/// </summary>
public sealed class WindowsMlSessionFactory : IInferenceSessionFactory
{
    public InferenceBackend Backend => InferenceBackend.WindowsML;

    public InferenceSession Create(string modelPath)
        => throw new NotSupportedException(
            "Windows ML 後端為 Phase 2 待啟用,尚未實作。" +
            "啟用需:Windows App SDK 1.8.1+、Windows 11 24H2(build 26100)+、" +
            "以另一 publish flavor 取代自包管的 OnnxRuntime.DirectML,並先以 EP 目錄 async 註冊暖機。" +
            "目前請改用 DirectML(預設)或 CPU 後端。");
}
