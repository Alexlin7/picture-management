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

        // Backend 一律由設定明示:appsettings.json 出貨即帶 "directml"(鐵則 6,跨 NV/AMD),
        // 各機再由 launchSettings 覆寫;無 GPU 可設 Inference:Wd14:Backend=cpu。
        // 故 Select 永遠走 configured 短路、不觸及 gpuVendor —— runtime GPU 偵測刻意不做(moot):
        // 廠商於 publish 時由套件綁定(DirectML build 跨全廠商;CUDA 走專屬 publish profile),
        // runtime 偵測「是哪家顯卡」無消費者(決議與理由見 ml-layer-architecture-assessment §6)。
        // 傳 null 保留 selector 的 auto seam(Backend 若留空則保守退 CPU),正常路徑不依賴它。
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
