using ImageMagick;
using Pm.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pm.Imaging.Tests;

// ImageLoader 是影像載入/識別 facade:對外只有意圖,引擎選擇是內政。
// 故測試走「行為」:同一組 API 對 HEIF(avif/heic,內部走 Magick)與非 HEIF(png,走 ImageSharp)
// 都要正確 —— 不去戳「用了哪個引擎」這種實作細節。
// HEIF fixture 以 Magick 產(Magick-Q8 有 HEIF 解碼但無 HEIC 編碼,故 heic 案以 avif 內容寫進 .heic 名)。
public class ImageLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pm-imgloader-" + Guid.NewGuid().ToString("N"));

    public ImageLoaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string WriteAvif(IMagickColor<byte> color, string ext = ".avif", uint w = 120, uint h = 80)
    {
        var path = Path.Combine(_dir, $"fixture{ext}");
        using var mi = new MagickImage(color, w, h);
        mi.Write(path, MagickFormat.Avif);
        return path;
    }

    private async Task<string> WritePngAsync(uint w = 200, uint h = 150)
    {
        var path = Path.Combine(_dir, "fixture.png");
        using var img = new Image<Rgba32>((int)w, (int)h, new Rgba32(10, 20, 30));
        await img.SaveAsPngAsync(path);
        return path;
    }

    // ---- HEIF 路徑(內部走 Magick)----

    [Fact]
    public async Task LoadAsync_解_avif_得正確尺寸與紅色像素()
    {
        var path = WriteAvif(MagickColors.Red);

        using var img = await ImageLoader.LoadAsync(path);

        Assert.Equal(120, img.Width);
        Assert.Equal(80, img.Height);
        var p = img.CloneAs<Rgba32>()[0, 0];
        Assert.True(p.R >= 200 && p.G <= 70 && p.B <= 70, $"RGB=({p.R},{p.G},{p.B}) 應接近紅");
    }

    [Fact]
    public void LoadRgb24_解_avif_得正確尺寸與紅色像素()
    {
        var path = WriteAvif(MagickColors.Red);

        using var img = ImageLoader.LoadRgb24(path);

        Assert.Equal(120, img.Width);
        Assert.Equal(80, img.Height);
        var p = img[0, 0];
        Assert.True(p.R >= 200 && p.G <= 70 && p.B <= 70, $"RGB=({p.R},{p.G},{p.B}) 應接近紅");
    }

    [Fact]
    public void Identify_avif_回正確尺寸與mime()
    {
        var path = WriteAvif(MagickColors.Blue);

        var info = ImageLoader.Identify(path);

        Assert.Equal(120, info.Width);
        Assert.Equal(80, info.Height);
        Assert.Equal("image/avif", info.Mime);
    }

    [Fact]
    public void Identify_heic_mime_為_image_heic()
    {
        // Magick-Q8 無 HEIC 編碼,故以 AVIF 內容寫進 .heic 名:尺寸由內容讀、mime 由副檔名映射。
        var path = WriteAvif(MagickColors.Green, ".heic");

        var info = ImageLoader.Identify(path);

        Assert.Equal(120, info.Width);
        Assert.Equal("image/heic", info.Mime);
    }

    // ---- 非 HEIF 路徑(內部走 ImageSharp;確認 facade 也吃下這條、mime 正確)----

    [Fact]
    public async Task LoadRgb24_與_Identify_對_png_仍正確()
    {
        var path = await WritePngAsync(200, 150);

        using var img = ImageLoader.LoadRgb24(path);
        var info = ImageLoader.Identify(path);

        Assert.Equal(200, img.Width);
        Assert.Equal(150, img.Height);
        Assert.Equal(200, info.Width);
        Assert.Equal(150, info.Height);
        Assert.Equal("image/png", info.Mime);
    }
}
