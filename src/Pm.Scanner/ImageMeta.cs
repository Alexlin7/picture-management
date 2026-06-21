namespace Pm.Scanner;

public readonly record struct ImageMeta(
    int? Width,
    int? Height,
    string? Mime,
    DateTimeOffset? TakenAt,
    string? CameraModel,
    double? GpsLat,
    double? GpsLon,
    string? ExifJson);
