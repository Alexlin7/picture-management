using Microsoft.Extensions.DependencyInjection;
using Pm.Ml;

namespace Pm.Api;

// WD14 自動標籤的 host wiring。預設關閉(Inference:Enabled=false),
// 開啟後才註冊 ONNX 推論工廠、Wd14Tagger(singleton、session 重用)與 TaggingWorker 背景服務。
public static class Wd14Setup
{
    public static IServiceCollection AddWd14Tagging(this IServiceCollection services, IConfiguration config)
    {
        // opt-in gate:預設關閉,免下載模型、零開銷。
        if (!config.GetValue<bool>("Inference:Enabled")) return services;

        var options = config.GetSection("Inference:Wd14").Get<Wd14Options>() ?? new Wd14Options();
        services.AddSingleton(options);

        // 預設 DirectML(鐵則 6,跨 NV/AMD);無 GPU 可設 Inference:Backend=cpu。
        // GPU 廠牌自動偵測列為日後工作,故 auto 模式暫傳 gpuVendor=null。
        var backend = InferenceBackendSelector.Select(config["Inference:Backend"], gpuVendor: null);
        services.AddSingleton(FactoryFor(backend));

        services.AddSingleton<IWd14Tagger, Wd14Tagger>();
        services.AddHostedService<TaggingWorker>();
        return services;
    }

    // 本 build 只帶 CPU + DirectML;Cuda/WindowsML 為骨架,被選到時明確報錯而非默默退化。
    private static IInferenceSessionFactory FactoryFor(InferenceBackend backend) => backend switch
    {
        InferenceBackend.Cpu => new CpuSessionFactory(),
        InferenceBackend.DirectMl => new DirectMlSessionFactory(),
        _ => throw new NotSupportedException(
            $"推論 backend '{backend}' 在本 build 未啟用(僅 cpu / directml);CUDA 需專屬 publish profile,Windows ML 為 Phase 2。")
    };
}
