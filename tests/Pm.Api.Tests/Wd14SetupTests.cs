using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pm.Api;
using Pm.Ml;
using Xunit;

namespace Pm.Api.Tests;

public class Wd14SetupTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    [Fact]
    public void Disabled_by_default_registers_nothing()
    {
        var services = new ServiceCollection();
        services.AddWd14Tagging(Config());   // 無 Inference:Wd14:Enabled → 預設關

        var sp = services.BuildServiceProvider();
        Assert.Null(sp.GetService<IWd14Tagger>());
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(TaggingWorker));
    }

    [Fact]
    public void Enabled_registers_tagger_factory_and_worker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWd14Tagging(Config(
            ("Inference:Wd14:Enabled", "true"),
            ("Inference:Wd14:Backend", "cpu")));

        var sp = services.BuildServiceProvider();
        Assert.IsType<Wd14Tagger>(sp.GetRequiredService<IWd14Tagger>());
        Assert.IsType<CpuSessionFactory>(sp.GetRequiredService<IInferenceSessionFactory>());
        Assert.Contains(services, d => d.ImplementationType == typeof(TaggingWorker));
    }
}
