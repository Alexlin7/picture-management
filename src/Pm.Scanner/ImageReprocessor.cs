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
        // 縮圖失敗(損毀但標頭可讀、格式限制等)不中止重新處理 —— metadata 已補回;
        // 僅回報 ThumbGenerated=false,不往上拋(對齊 spec §6:不拋、不回復 metadata)。
        var thumb = false;
        try { thumb = await thumbs.GenerateAsync(absPath, photo.FileHash, ct) is not null; }
        catch { thumb = false; }
        return new ReprocessResult(true, thumb);
    }
}
