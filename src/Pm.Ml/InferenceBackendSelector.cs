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
                _ => throw new ArgumentException($"未知的推論 backend:'{configured}'")
            };
        }

        // 沒指定 → 有顯卡走 DirectML(跨 NV/AMD),沒有就 CPU。
        return string.IsNullOrWhiteSpace(gpuVendor)
            ? InferenceBackend.Cpu
            : InferenceBackend.DirectMl;
    }
}
