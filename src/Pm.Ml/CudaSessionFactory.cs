#if INFER_CUDA
using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

/// <summary>
/// CUDA 後端 —— 僅在 cuda flavor(<c>-p:InferenceFlavor=cuda</c>,引入
/// <c>Microsoft.ML.OnnxRuntime.Gpu</c>)下編譯。目標族群:24H2 以下的 NVIDIA 使用者
/// (24H2+ 主走 Windows ML;通用 fallback 走 DirectML)。
///
/// Gpu 套件的 onnxruntime.dll 帶 {CPU, CUDA, TensorRT} EP;機器另需安裝相容的
/// CUDA / cuDNN runtime。本工廠掛 CUDA EP(內建 CPU fallback);TensorRT 雖更快但需額外
/// native 庫且首跑要 build engine,故預設不掛,要時可加 <c>AppendExecutionProvider_Tensorrt</c>。
/// </summary>
public sealed class CudaSessionFactory : IInferenceSessionFactory
{
    private readonly int _deviceId;
    public CudaSessionFactory(int deviceId = 0) => _deviceId = deviceId;

    public InferenceBackend Backend => InferenceBackend.Cuda;

    public InferenceSession Create(string modelPath)
    {
        var so = new SessionOptions();
        so.AppendExecutionProvider_CUDA(_deviceId);   // 失敗會丟,session 建立期即報錯(不默默退化)
        return new InferenceSession(modelPath, so);
    }
}
#endif
