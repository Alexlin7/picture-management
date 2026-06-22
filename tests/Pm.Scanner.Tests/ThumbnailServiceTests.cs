using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Scanner.Tests;

public class ThumbnailServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-thumbs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void PathFor_buckets_by_hash_prefix()
    {
        var svc = new ThumbnailService(new ThumbnailOptions { Dir = _dir });
        var hash = new string('a', 60) + "bcde";   // 64 字
        var p = svc.PathFor(hash);
        Assert.Equal(Path.Combine(_dir, "aa", "aa", hash + ".webp"), p);
    }

    [Fact]
    public async Task Generates_downscaled_webp()
    {
        var src = Path.Combine(_dir, "src.png");
        Directory.CreateDirectory(_dir);
        using (var img = new Image<Rgba32>(1024, 768)) await img.SaveAsPngAsync(src);

        var svc = new ThumbnailService(new ThumbnailOptions { Dir = _dir, MaxEdge = 512 });
        var hash = "abcd" + new string('0', 60);
        var outPath = await svc.GenerateAsync(src, hash);

        Assert.NotNull(outPath);
        Assert.True(File.Exists(outPath));
        Assert.Equal(svc.PathFor(hash), outPath);

        var info = Image.Identify(outPath!);
        Assert.Equal(512, info.Width);    // 長邊縮到 512
        Assert.Equal(384, info.Height);   // 比例保持(1024:768 → 512:384)
    }
}
