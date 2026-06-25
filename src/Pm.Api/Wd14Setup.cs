using Microsoft.Extensions.DependencyInjection;
using Pm.Ml;

namespace Pm.Api;

// WD14 自動標籤的 host wiring。預設關閉(Inference:Wd14:Enabled=false),
// 開啟後才註冊 ONNX 推論工廠、Wd14Tagger(singleton、session 重用)與 TaggingWorker 背景服務。
// 能力開關按模型獨立(Inference:Wd14:*),未來 CLIP 走 Inference:Clip:*,互不綁。
public static class Wd14Setup
{
    public static IServiceCollection AddWd14Tagging(this IServiceCollection services, IConfiguration config)
    {
        // opt-in gate:預設關閉,免下載模型、零開銷。
        if (!config.GetValue<bool>("Inference:Wd14:Enabled")) return services;

        var options = config.GetSection("Inference:Wd14").Get<Wd14Options>() ?? new Wd14Options();
        services.AddSingleton(options);

        // 預設 DirectML(鐵則 6,跨 NV/AMD);無 GPU 可設 Inference:Wd14:Backend=cpu。
        // GPU 廠牌自動偵測列為日後工作,故 auto 模式暫傳 gpuVendor=null。
        var backend = InferenceBackendSelector.Select(config["Inference:Wd14:Backend"], gpuVendor: null);
        services.AddSingleton(FactoryFor(backend));

        services.AddSingleton<IWd14Tagger, Wd14Tagger>();
        services.AddHostedService<TaggingWorker>();
        return services;
    }

    // 本 build 帶的推論工廠;各 factory 自帶 .Backend,加新 backend 只需在此清單加一筆
    // (不必再同步維護一份 backend→factory 的 switch)。Cuda/WindowsML 為骨架,不在清單內,
    // 被選到時明確報錯而非默默退化。
    private static readonly IInferenceSessionFactory[] Available =
        [new CpuSessionFactory(), new DirectMlSessionFactory()];

    private static IInferenceSessionFactory FactoryFor(InferenceBackend backend)
        => Array.Find(Available, f => f.Backend == backend)
           ?? throw new NotSupportedException(
               $"推論 backend '{backend}' 在本 build 未啟用(僅 cpu / directml);CUDA 需專屬 publish profile,Windows ML 為 Phase 2。");
}
