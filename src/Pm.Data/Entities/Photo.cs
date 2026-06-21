namespace Pm.Data.Entities;

public class Photo
{
    public long Id { get; set; }
    public string FileHash { get; set; } = null!;   // SHA-256 hex,64 字
    public long? FileSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Mime { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public string? CameraModel { get; set; }
    public double? GpsLat { get; set; }             // SQLite 無 POINT,拆兩欄
    public double? GpsLon { get; set; }
    public string? Exif { get; set; }               // JSON 存 TEXT
    public DateTimeOffset ImportedAt { get; set; }

    public List<PhotoLocation> Locations { get; } = new();
    public List<PhotoTag> Tags { get; } = new();
}
