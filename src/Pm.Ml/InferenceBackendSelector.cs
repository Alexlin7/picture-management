namespace Pm.Ml;

// 純函式:啟動參數優先,否則依偵測到的顯卡。
// 本 build 只帶 DirectML(+CPU);CUDA 僅於專屬 publish profile 才可用,
// 故偵測到 GPU 一律回 DirectMl,Cuda 只能由 configured 明示。
public static class InferenceBackendSelector
{
    public static InferenceBackend Select(string? configured, string? gpuVendor)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().ToLowerInvariant() switch
            {
                "cpu"  => InferenceBackend.Cpu,
                "dml" or "directml" => InferenceBackend.DirectMl,
                "cuda" => InferenceBackend.Cuda,
                "winml" or "windowsml" => InferenceBackend.WindowsML,  // Phase 2:僅明示可選
                _ => throw new ArgumentException($"未知的推論 backend:'{configured}'")
            };
        }

        // 沒指定 → 有顯卡走 DirectML(跨 NV/AMD),沒有就 CPU。
        // Phase 2 待啟用:此處未來會加 Windows ML 偵測 —— 若 OS build ≥ 26100(Win11 24H2)
        // 且 App SDK / EP 目錄可用,且為合適硬體(如 NVIDIA RTX 30xx+ 走 TensorRT-RTX),
        // 才回 WindowsML;偵測失敗一律 fall back 到 DirectML/CPU。目前 auto 不選 WindowsML。
        return string.IsNullOrWhiteSpace(gpuVendor)
            ? InferenceBackend.Cpu
            : InferenceBackend.DirectMl;
    }
}
