namespace Pm.Ml;

// 純函式:啟動參數優先,否則依偵測到的顯卡。enum 是所有 flavor 的聯集(語彙);某 build「真的帶」
// 哪些 backend 由 InferenceFlavor 編譯期決定(見 InferenceFactories)。選到本 build 沒帶的 backend,
// 由 Wd14Setup.FactoryFor 明確報錯。auto 分支(gpuVendor)為保留 seam,實際不觸發(見設計文件 §6)。
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
                "winml" or "windowsml" => InferenceBackend.WindowsML,  // 僅 windowsml flavor build 真的帶
                _ => throw new ArgumentException($"未知的推論 backend:'{configured}'")
            };
        }

        // 沒指定 → 有顯卡走 DirectML(跨 NV/AMD),沒有就 CPU。
        // auto 不選 WindowsML:WinML 的 EP 由 ORT device policy 在 windowsml flavor 內挑,
        // 不靠這裡的廠牌偵測(見設計文件 §6);此 auto 分支為保留 seam,正常路徑由 configured 短路。
        return string.IsNullOrWhiteSpace(gpuVendor)
            ? InferenceBackend.Cpu
            : InferenceBackend.DirectMl;
    }
}
