namespace Pm.Ml;

/// <summary>
/// 本 build「真的帶」的推論工廠清單 = CPU(永遠內建 fallback)+ 該 flavor 的 GPU EP factory。
///
/// flavor 由 <c>Pm.Ml.csproj</c> 的 <c>InferenceFlavor</c> 屬性於編譯期決定(directml 預設 / cuda /
/// windowsml),推出 <c>INFER_*</c> 常數選對應 factory —— 三套 native ORT 互斥,故同一 build 只編進
/// 一個 GPU factory。此清單與 <c>INFER_*</c> 常數同住 Pm.Ml(常數只作用於定義它的專案),由
/// Pm.Api 的 <c>Wd14Setup</c> 取用做 backend→factory 對應。
///
/// 加 backend = 在對應 flavor 的 #if 分支加一筆(各 factory 自帶 <c>.Backend</c>,不維護額外 switch)。
/// 選到本 build 沒帶的 backend,由呼叫端明確報錯而非默默退化(見 ml-layer-architecture-assessment §9.3)。
/// </summary>
public static class InferenceFactories
{
    public static IReadOnlyList<IInferenceSessionFactory> ForThisBuild() =>
    [
        new CpuSessionFactory(),
#if INFER_DIRECTML
        new DirectMlSessionFactory(),
#elif INFER_CUDA
        new CudaSessionFactory(),
#elif INFER_WINDOWSML
        new WindowsMlSessionFactory(),
#endif
    ];
}
