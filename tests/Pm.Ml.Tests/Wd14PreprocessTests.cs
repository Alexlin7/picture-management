using ImageMagick;
using Pm.Ml;
using Xunit;

namespace Pm.Ml.Tests;

// WD14 前處理對 AVIF 的整合:ImageSharp 不解 AVIF,需繞道 Magick.NET(ImageDecoder)。
// 未接橋接時 Image.Load<Rgb24>(avif) 會丟例外 → tagging job 直接失敗。
public class Wd14PreprocessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pm-wd14pre-" + Guid.NewGuid().ToString("N"));

    public Wd14PreprocessTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void ToTensor_吃avif_得正確形狀且中心為紅()
    {
        var src = Path.Combine(_dir, "x.avif");
        using (var mi = new MagickImage(MagickColors.Red, 200u, 100u))
            mi.Write(src, MagickFormat.Avif);

        var t = Wd14Preprocess.ToTensor(src, 448);

        Assert.Equal(new[] { 1, 448, 448, 3 }, t.Dimensions.ToArray());
        // 中心落在紅色區(200x100 置中於白底方形後縮放);通道為 BGR。
        Assert.True(t[0, 224, 224, 2] >= 200, $"R={t[0, 224, 224, 2]} 應接近 255");
        Assert.True(t[0, 224, 224, 0] <= 70, $"B={t[0, 224, 224, 0]} 應接近 0");
    }
}
