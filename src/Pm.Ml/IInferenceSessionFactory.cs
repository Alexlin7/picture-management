using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

public interface IInferenceSessionFactory
{
    InferenceBackend Backend { get; }

    // EP 暖機接縫:多數後端為 no-op(EP 由出貨套件烤死,Create 直接可用);
    // 唯 Windows ML 需 async 一次性註冊 EP 目錄(EnsureAndRegisterCertifiedAsync)後才能 Create。
    // 由呼叫端在建立第一個 session 前 await 一次(idempotent;見 Wd14Tagger.EnsureLoadedAsync)。
    // 預設 no-op,故 Cpu/DirectML/Cuda 不需實作。
    Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    InferenceSession Create(string modelPath);
}
