using Pm.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Pm.Scanner.Tests;

public class MetadataReaderTests
{
    private static string Temp(string ext) =>
        Path.Combine(Path.GetTempPath(), $"pm-meta-{Guid.NewGuid():N}{ext}");

    [Fact]
    public async Task Reads_dimensions_and_mime_for_png()
    {
        var path = Temp(".png");
        using (var img = new Image<Rgba32>(800, 600)) await img.SaveAsPngAsync(path);
        try
        {
            var meta = new ExifImageMetadataReader().Read(path);
            Assert.Equal(800, meta.Width);
            Assert.Equal(600, meta.Height);
            Assert.Equal("image/png", meta.Mime);
            Assert.Null(meta.TakenAt);
            Assert.Null(meta.CameraModel);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Reads_camera_and_date_from_jpeg_exif()
    {
        var path = Temp(".jpg");
        using (var img = new Image<Rgba32>(64, 64))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Make, "Canon");
            exif.SetValue(ExifTag.Model, "EOS R5");
            exif.SetValue(ExifTag.DateTimeOriginal, "2026:06:21 10:30:00");
            img.Metadata.ExifProfile = exif;
            await img.SaveAsJpegAsync(path);
        }
        try
        {
            var meta = new ExifImageMetadataReader().Read(path);
            Assert.Equal("image/jpeg", meta.Mime);
            Assert.Contains("Canon", meta.CameraModel);
            Assert.Contains("EOS R5", meta.CameraModel);
            Assert.NotNull(meta.TakenAt);
            Assert.Equal(2026, meta.TakenAt!.Value.Year);
            Assert.NotNull(meta.ExifJson);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reads_dimensions_and_mime_for_avif()
    {
        // ImageSharp 不解 AVIF;確認 metadata reader 已繞道 Magick.NET 讀 header(尺寸/mime)。
        var path = Temp(".avif");
        using (var mi = new ImageMagick.MagickImage(ImageMagick.MagickColors.Purple, 640u, 480u))
            mi.Write(path, ImageMagick.MagickFormat.Avif);
        try
        {
            var meta = new ExifImageMetadataReader().Read(path);
            Assert.Equal(640, meta.Width);
            Assert.Equal(480, meta.Height);
            Assert.Equal("image/avif", meta.Mime);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Non_image_returns_all_null_without_throwing()
    {
        var path = Temp(".png");
        await File.WriteAllTextAsync(path, "not really an image");
        try
        {
            var meta = new ExifImageMetadataReader().Read(path);
            Assert.Null(meta.Width);
            Assert.Null(meta.Mime);
        }
        finally { File.Delete(path); }
    }
}
