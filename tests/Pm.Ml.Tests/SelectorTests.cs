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
    public void Configured_param_wins(string configured, InferenceBackend expected)
    {
        Assert.Equal(expected, InferenceBackendSelector.Select(configured, gpuVendor: "NVIDIA"));
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
