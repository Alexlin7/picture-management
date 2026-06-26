using ImageMagick;
using Pm.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pm.Imaging.Tests;

// ImageDecoder 是 HEIF 家族(AVIF/HEIC/HEIF)→ ImageSharp 的橋接:ImageSharp 不解 HEIF,
// 故這族繞道 Magick.NET(內建 libheif)解成像素,再包回 ImageSharp 讓既有管線不動。
// fixture 以 Magick 產生(同一顆 libheif),驗證的是「我們的橋接把尺寸/通道順序/mime 接對了」。
public class ImageDecoderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pm-imaging-" + Guid.NewGuid().ToString("N"));

    public ImageDecoderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string WriteHeif(MagickFormat fmt, string ext, IMagickColor<byte> color, uint w = 120, uint h = 80)
    {
        var path = Path.Combine(_dir, $"fixture{ext}");
        using var mi = new MagickImage(color, w, h);
        mi.Write(path, fmt);
        return path;
    }

    [Theory]
    [InlineData("a.avif", true)]
    [InlineData("a.AVIF", true)]
    [InlineData("a.heic", true)]
    [InlineData("a.heif", true)]
    [InlineData("a.jpg", false)]
    [InlineData("a.png", false)]
    [InlineData("a.webp", false)]
    public void IsHeifFamily_只對_avif_heic_heif_為真(string name, bool expected)
        => Assert.Equal(expected, ImageDecoder.IsHeifFamily(name));

    [Fact]
    public void LoadRgba32_解_avif_得正確尺寸與紅色像素()
    {
        var path = WriteHeif(MagickFormat.Avif, ".avif", MagickColors.Red);

        using var img = ImageDecoder.LoadRgba32(path);

        Assert.Equal(120, img.Width);
        Assert.Equal(80, img.Height);
        var p = img[0, 0];
        Assert.True(p.R >= 200, $"R={p.R} 應接近 255(紅)");
        Assert.True(p.G <= 70 && p.B <= 70, $"G={p.G} B={p.B} 應接近 0");
        Assert.Equal(255, p.A);
    }

    [Fact]
    public void LoadRgb24_解_avif_得正確尺寸與紅色像素()
    {
        var path = WriteHeif(MagickFormat.Avif, ".avif", MagickColors.Red);

        using var img = ImageDecoder.LoadRgb24(path);

        Assert.Equal(120, img.Width);
        Assert.Equal(80, img.Height);
        var p = img[0, 0];
        Assert.True(p.R >= 200 && p.G <= 70 && p.B <= 70, $"RGB=({p.R},{p.G},{p.B}) 應接近紅");
    }

    [Fact]
    public void IdentifyHeif_avif_回正確尺寸與_mime()
    {
        var path = WriteHeif(MagickFormat.Avif, ".avif", MagickColors.Blue);

        var (w, h, mime) = ImageDecoder.IdentifyHeif(path);

        Assert.Equal(120, w);
        Assert.Equal(80, h);
        Assert.Equal("image/avif", mime);
    }

    [Fact]
    public void IdentifyHeif_heic_mime_為_image_heic()
    {
        // Magick.NET-Q8 的 libheif 有 HEIC 解碼但無 HEIC 編碼(x265 授權因素未打包),
        // 故無法用 Magick 產 .heic fixture;這裡把 AVIF 內容寫進 .heic 檔名 —— 尺寸由
        // MagickImageInfo 依內容讀、mime 由 IdentifyHeif 依副檔名映射,正好驗證 heic 分支。
        var path = WriteHeif(MagickFormat.Avif, ".heic", MagickColors.Green);

        var (w, h, mime) = ImageDecoder.IdentifyHeif(path);

        Assert.Equal(120, w);
        Assert.Equal(80, h);
        Assert.Equal("image/heic", mime);
    }
}
