using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Pm.Api;
using Xunit;

namespace Pm.Api.Tests;

public class StoragePathsTests
{
    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Pm.Api.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    [Fact]
    public void Development_uses_current_directory_as_base()
    {
        var env = new FakeEnv { EnvironmentName = "Development" };
        var p = StoragePaths.Resolve(env, Config());

        Assert.Equal(Directory.GetCurrentDirectory(), p.BaseDir);
        Assert.Equal($"Data Source={Path.Combine(p.BaseDir, "pm.sqlite")}", p.SqliteDataSource);
        Assert.Equal(Path.Combine(p.BaseDir, "thumbs"), p.ThumbsDir);
        Assert.Equal(Path.Combine(p.BaseDir, "models", "wd14"), p.ModelDir);
        Assert.Equal(Path.Combine(p.BaseDir, "logs"), p.LogDir);
    }

    [Fact]
    public void Production_uses_localappdata_subfolder_as_base()
    {
        var env = new FakeEnv { EnvironmentName = "Production" };
        var p = StoragePaths.Resolve(env, Config());

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "sus-picture-management");
        Assert.Equal(expected, p.BaseDir);
    }

    [Fact]
    public void Explicit_base_dir_override_wins_over_environment()
    {
        var env = new FakeEnv { EnvironmentName = "Production" };
        var dir = Path.Combine(Path.GetTempPath(), "pm-override-test");
        var p = StoragePaths.Resolve(env, Config(("Storage:BaseDir", dir)));

        Assert.Equal(Path.GetFullPath(dir), p.BaseDir);
        Assert.Equal(Path.Combine(Path.GetFullPath(dir), "logs"), p.LogDir);
    }

    [Fact]
    public void Absolute_subpath_in_config_is_not_reprefixed()
    {
        var env = new FakeEnv { EnvironmentName = "Production" };
        var absThumbs = Path.Combine(Path.GetTempPath(), "external-thumbs");
        var p = StoragePaths.Resolve(env, Config(("Thumbnails:Dir", absThumbs)));

        Assert.Equal(absThumbs, p.ThumbsDir);
    }

    [Fact]
    public void Relative_subpath_in_config_is_combined_with_base()
    {
        var env = new FakeEnv { EnvironmentName = "Development" };
        var p = StoragePaths.Resolve(env, Config(("Thumbnails:Dir", "custom-thumbs")));

        Assert.Equal(Path.Combine(p.BaseDir, "custom-thumbs"), p.ThumbsDir);
    }
}
