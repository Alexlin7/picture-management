using System.Text.Json;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SixLabors.ImageSharp;

namespace Pm.Scanner;

public sealed class ExifImageMetadataReader : IImageMetadataReader
{
    public ImageMeta Read(string absPath)
    {
        int? width = null, height = null;
        string? mime = null;

        // 尺寸 / MIME(ImageSharp)
        try
        {
            var info = Image.Identify(absPath);
            width = info.Width;
            height = info.Height;
            mime = info.Metadata.DecodedImageFormat?.DefaultMimeType;
        }
        catch { /* 非 ImageSharp 可解碼的圖 */ }

        DateTimeOffset? takenAt = null;
        string? cameraModel = null;
        double? gpsLat = null, gpsLon = null;
        string? exifJson = null;

        // EXIF(MetadataExtractor)
        try
        {
            var dirs = MetadataExtractor.ImageMetadataReader.ReadMetadata(absPath);
            var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            var sub = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var gps = dirs.OfType<GpsDirectory>().FirstOrDefault();

            var make = ifd0?.GetDescription(ExifDirectoryBase.TagMake);
            var model = ifd0?.GetDescription(ExifDirectoryBase.TagModel);
            var combined = string.Join(" ",
                new[] { make, model }.Where(s => !string.IsNullOrWhiteSpace(s)));
            cameraModel = string.IsNullOrWhiteSpace(combined) ? null : combined;

            if (sub is not null &&
                sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                takenAt = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));

            var geo = gps?.GetGeoLocation();
            if (geo is { } g && !g.IsZero)
            {
                gpsLat = g.Latitude;
                gpsLon = g.Longitude;
            }

            var map = new Dictionary<string, string>();
            foreach (var d in dirs)
                foreach (var t in d.Tags)
                    if (t.Description is not null)
                        map[$"{d.Name}/{t.Name}"] = t.Description;
            if (map.Count > 0)
                exifJson = JsonSerializer.Serialize(map);
        }
        catch { /* 無/壞 EXIF */ }

        return new ImageMeta(width, height, mime, takenAt, cameraModel, gpsLat, gpsLon, exifJson);
    }
}
