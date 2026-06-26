namespace Pm.Scanner;
using Pm.Data.Entities;

public sealed class ImageReprocessor(IImageMetadataReader meta, IThumbnailService thumbs) : IImageReprocessor
{
    public async Task<ReprocessResult> ReprocessAsync(Photo photo, string absPath, CancellationToken ct = default)
    {
        var m = meta.Read(absPath);
        photo.Width = m.Width;
        photo.Height = m.Height;
        photo.Mime = m.Mime;
        photo.TakenAt = m.TakenAt;
        photo.CameraModel = m.CameraModel;
        photo.GpsLat = m.GpsLat;
        photo.GpsLon = m.GpsLon;
        photo.Exif = m.ExifJson;

        var decoded = m.Width is not null;
        if (!decoded) return new ReprocessResult(false, false);

        // GenerateAsync 內部 File.Replace → 覆蓋既有縮圖(force 語意)。
        var thumb = await thumbs.GenerateAsync(absPath, photo.FileHash, ct) is not null;
        return new ReprocessResult(true, thumb);
    }
}
