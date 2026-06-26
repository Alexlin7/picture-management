using ImageMagick;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pm.Imaging;

/// <summary>影像載入/識別的 facade:對外只表達意圖(載圖 / 讀尺寸),內部自行選解碼引擎。</summary>
/// <remarks>
/// 消費端(Scanner / ML)不該知道有幾個引擎、何時用哪個 —— 那是本層的內政。
/// 規則:HEIF 家族(avif/heic/heif)ImageSharp 不解,改用 Magick.NET(內建 libheif)
/// 解成像素再包回 ImageSharp;其餘格式直接走 ImageSharp。ImageSharp 是全 app 的影像
/// 表示/處理型別(resize/webp/pixel rows 都靠它),故回傳型別維持 ImageSharp;Magick 僅
/// 作內部解碼策略,絕不外洩。
/// </remarks>
public static class ImageLoader
{
    private static readonly HashSet<string> HeifFamilyExts =
        new(StringComparer.OrdinalIgnoreCase) { ".avif", ".heic", ".heif" };

    private static bool IsHeif(string path) => HeifFamilyExts.Contains(Path.GetExtension(path));

    /// <summary>載入為可處理的影像(保留 alpha,供縮圖)。引擎由本層決定。</summary>
    public static async Task<Image> LoadAsync(string path, CancellationToken ct = default)
        => IsHeif(path) ? LoadHeifRgba32(path) : await Image.LoadAsync(path, ct);

    /// <summary>載入為 RGB24(供 WD14 前處理,免 alpha)。引擎由本層決定。</summary>
    public static Image<Rgb24> LoadRgb24(string path)
    {
        if (!IsHeif(path)) return Image.Load<Rgb24>(path);
        using var mi = new MagickImage(path);
        var pixels = mi.GetPixels().ToByteArray(PixelMapping.RGB)
            ?? throw new InvalidOperationException($"無法取得像素:{path}");
        return Image.LoadPixelData<Rgb24>(pixels, (int)mi.Width, (int)mi.Height);
    }

    /// <summary>只讀尺寸與 mime(不做完整像素解碼)。所有格式都從這走,引擎由本層決定。</summary>
    public static ImageInfo Identify(string path)
    {
        if (IsHeif(path))
        {
            var info = new MagickImageInfo(path);
            return new ImageInfo((int)info.Width, (int)info.Height, MimeOf(path));
        }

        var i = Image.Identify(path);
        return new ImageInfo(i.Width, i.Height, i.Metadata.DecodedImageFormat?.DefaultMimeType);
    }

    private static Image<Rgba32> LoadHeifRgba32(string path)
    {
        using var mi = new MagickImage(path);
        var pixels = mi.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidOperationException($"無法取得像素:{path}");
        return Image.LoadPixelData<Rgba32>(pixels, (int)mi.Width, (int)mi.Height);
    }

    private static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".avif" => "image/avif",
        ".heic" => "image/heic",
        ".heif" => "image/heif",
        _ => "application/octet-stream",
    };
}

/// <summary>影像基本資訊:尺寸與 mime(mime 可能為 null,例如 ImageSharp 未知格式)。</summary>
public readonly record struct ImageInfo(int Width, int Height, string? Mime);
