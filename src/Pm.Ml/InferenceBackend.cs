namespace Pm.Ml;

public enum InferenceBackend
{
    Cpu,
    DirectMl,
    Cuda,

    // Phase 2 待啟用:Windows ML(OS 共享 ORT + 動態 EP 目錄,TensorRT-RTX / OpenVINO / QNN / NPU)。
    // 需 Windows App SDK 1.8.1+ 與 Windows 11 24H2(build 26100)+;走另一個 publish flavor。
    // 目前 auto-detect 不會選它,只能由 configured 明示("winml");Create() 尚未實作。
    // 設計與取捨見 WindowsMlSessionFactory 註解。
    WindowsML
}
