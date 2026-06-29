using Pm.Ml;
using Xunit;

namespace Pm.Ml.Tests;

public class SelectorTests
{
    [Theory]
    [InlineData("cpu", InferenceBackend.Cpu)]
    [InlineData("dml", InferenceBackend.DirectMl)]
    [InlineData("directml", InferenceBackend.DirectMl)]
    [InlineData("cuda", InferenceBackend.Cuda)]
    [InlineData("winml", InferenceBackend.WindowsML)]      // 僅 windowsml flavor build 真的帶
    [InlineData("windowsml", InferenceBackend.WindowsML)]
    public void Configured_param_wins(string configured, InferenceBackend expected)
    {
        Assert.Equal(expected, InferenceBackendSelector.Select(configured, gpuVendor: "NVIDIA"));
    }

    [Fact]
    public void Auto_detect_never_picks_windowsml()
    {
        // auto-detect 一律不選 WindowsML(WinML 由 configured 明示 + 走 ORT device policy);維持 DirectML/CPU 行為。
        Assert.Equal(InferenceBackend.DirectMl,
            InferenceBackendSelector.Select(configured: null, gpuVendor: "NVIDIA RTX 4090"));
    }

    [Fact]
    public void No_config_with_gpu_picks_directml()
    {
        Assert.Equal(InferenceBackend.DirectMl,
            InferenceBackendSelector.Select(configured: null, gpuVendor: "AMD Radeon"));
    }

    [Fact]
    public void No_config_no_gpu_falls_back_to_cpu()
    {
        Assert.Equal(InferenceBackend.Cpu,
            InferenceBackendSelector.Select(configured: null, gpuVendor: null));
    }

    [Fact]
    public void Unknown_configured_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InferenceBackendSelector.Select("metal", gpuVendor: null));
    }
}
