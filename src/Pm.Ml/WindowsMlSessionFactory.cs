#if INFER_WINDOWSML
using Microsoft.ML.OnnxRuntime;
using Microsoft.Windows.AI.MachineLearning;

namespace Pm.Ml;

/// <summary>
/// Windows ML 後端 —— 僅在 windowsml flavor(<c>-p:InferenceFlavor=windowsml</c>,引入
/// <c>Microsoft.Windows.AI.MachineLearning</c>)下編譯。目標族群:Windows 11 24H2(build 26100)+
/// (硬體最佳化 EP 需此版);舊系統走 DirectML / CUDA 變體。
///
/// 與經典自包含的差別:ORT 由 OS / App SDK 共享,vendor EP(NVIDIA TensorRT-RTX、Intel OpenVINO、
/// AMD Vitis、Qualcomm QNN)是 runtime 由 <see cref="ExecutionProviderCatalog"/> 下載 / 註冊的
/// plugin,不烤進部署包;預設仍含 CPU / DirectML EP 作 fallback。故需一次性 async 暖機
/// (<see cref="InitializeAsync"/> 走 <c>EnsureAndRegisterCertifiedAsync</c>)後 EP 清單才齊。
///
/// EP 選擇策略(對齊任務要求「顯式選擇優先確保可預期性,policy 次之,失敗 fallback 並回報」):
///   1. 顯式:列舉 <c>GetEpDevices()</c>,挑非 CPU 的 vendor EP 裝置,<c>AppendExecutionProvider</c> 明示。
///   2. 顯式無合適裝置 / 失敗 → 退 device policy(<see cref="ExecutionProviderDevicePolicy.MaxPerformance"/>)。
///   3. 再失敗 → 退純 CPU session(永遠可用),標記 degraded。
///   EP 清單隨驅動 / EP 更新動態變動,故不 hard-code 假設某 EP 一定存在。
/// 最後選到的 EP 記在 <see cref="SelectedProvider"/> 供 host / UX 回報。
/// </summary>
public sealed class WindowsMlSessionFactory : IInferenceSessionFactory
{
    private readonly ExecutionProviderDevicePolicy _policy;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private volatile bool _initialized;

    public WindowsMlSessionFactory(
        ExecutionProviderDevicePolicy policy = ExecutionProviderDevicePolicy.MAX_PERFORMANCE)
        => _policy = policy;

    public InferenceBackend Backend => InferenceBackend.WindowsML;

    /// <summary>最後一次 <see cref="Create"/> 實際選到的 EP 描述(供回報;null = 尚未建過 session)。</summary>
    public string? SelectedProvider { get; private set; }

    /// <summary>
    /// 一次性暖機:下載並註冊 OS 認證的 EP 目錄。idempotent;由 Wd14Tagger 在建第一個 session 前 await。
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            var catalog = ExecutionProviderCatalog.GetDefault();
            await catalog.EnsureAndRegisterCertifiedAsync();
            _initialized = true;
        }
        finally { _initGate.Release(); }
    }

    public InferenceSession Create(string modelPath)
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "WindowsML 後端需先 await InitializeAsync() 註冊 EP 目錄再 Create。");

        var env = OrtEnv.Instance();

        // 1) 顯式選擇:挑第一個非 CPU 的 vendor EP 裝置,可預期、不靠 policy 黑箱。
        var device = PickVendorDevice(env);
        if (device is not null)
        {
            try
            {
                var so = new SessionOptions();
                so.AppendExecutionProvider(env, new[] { device }, new Dictionary<string, string>());
                var session = new InferenceSession(modelPath, so);
                SelectedProvider = device.EpName;
                return session;
            }
            catch { /* 落到 policy / CPU fallback */ }
        }

        // 2) device policy 自動挑(顯式無合適裝置或失敗時)。
        try
        {
            var so = new SessionOptions();
            so.SetEpSelectionPolicy(_policy);
            var session = new InferenceSession(modelPath, so);
            SelectedProvider = $"policy:{_policy}";
            return session;
        }
        catch { /* 落到 CPU fallback */ }

        // 3) 純 CPU(永遠可用),標記 degraded 供回報。
        SelectedProvider = "CPU (fallback)";
        return new InferenceSession(modelPath);
    }

    // 挑非 CPU 的 EP 裝置(vendor GPU / NPU);無則回 null 交給 policy/CPU。
    private static OrtEpDevice? PickVendorDevice(OrtEnv env)
    {
        foreach (var d in env.GetEpDevices())
        {
            if (!d.EpName.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return null;
    }
}
#endif
