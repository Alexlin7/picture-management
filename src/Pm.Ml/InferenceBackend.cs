namespace Pm.Ml;

public enum InferenceBackend
{
    Cpu,
    DirectMl,
    Cuda,

    // Windows ML(OS 共享 ORT + 動態 EP 目錄,TensorRT-RTX / OpenVINO / MIGraphX / QNN / NPU)。
    // 需 Microsoft.Windows.AI.MachineLearning 與 Windows 11 24H2(build 26100)+;為獨立 publish flavor
    // (-p:InferenceFlavor=windowsml)。auto-detect 不選它(EP 由 ORT device policy 挑),由 configured
    // 明示("winml")。實作見 WindowsMlSessionFactory(僅 windowsml flavor 編譯)。
    WindowsML
}
