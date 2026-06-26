using ImageMagick;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pm.Imaging;

// HEIF 家族(AVIF / HEIC / HEIF)解碼橋接。
// SixLabors.ImageSharp 3.x 不支援 HEIF 家族解碼;這族繞道 Magick.NET(內建 libheif,
// 原生 dll 隨 NuGet,可嵌入單檔 exe)解成像素,再包回 ImageSharp 的 Image,讓既有
// 縮圖 / metadata / WD14 前處理管線完全不必改 —— 只在「載入」這個 seam 依副檔名分流。
public static class ImageDecoder
{
    private static readonly HashSet<string> HeifFamilyExts =
        new(StringComparer.OrdinalIgnoreCase) { ".avif", ".heic", ".heif" };

    /// <summary>是否為需繞道 Magick.NET 解碼的 HEIF 家族(avif/heic/heif)。其餘交給 ImageSharp。</summary>
    public static bool IsHeifFamily(string path) =>
        HeifFamilyExts.Contains(Path.GetExtension(path));

    /// <summary>解 HEIF 家族 → ImageSharp <see cref="Image{Rgba32}"/>(保留 alpha,供縮圖)。</summary>
    public static Image<Rgba32> LoadRgba32(string path)
    {
        using var mi = new MagickImage(path);
        var pixels = mi.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidOperationException($"無法取得像素:{path}");
        return Image.LoadPixelData<Rgba32>(pixels, (int)mi.Width, (int)mi.Height);
    }

    /// <summary>解 HEIF 家族 → ImageSharp <see cref="Image{Rgb24}"/>(供 WD14 前處理,免 alpha)。</summary>
    public static Image<Rgb24> LoadRgb24(string path)
    {
        using var mi = new MagickImage(path);
        var pixels = mi.GetPixels().ToByteArray(PixelMapping.RGB)
            ?? throw new InvalidOperationException($"無法取得像素:{path}");
        return Image.LoadPixelData<Rgb24>(pixels, (int)mi.Width, (int)mi.Height);
    }

    /// <summary>只讀 HEIF 家族的尺寸與 mime(不做完整像素解碼,供索引 metadata)。</summary>
    public static (int Width, int Height, string Mime) IdentifyHeif(string path)
    {
        var info = new MagickImageInfo(path);
        return ((int)info.Width, (int)info.Height, MimeOf(path));
    }

    private static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".avif" => "image/avif",
        ".heic" => "image/heic",
        ".heif" => "image/heif",
        _ => "application/octet-stream",
    };
}
