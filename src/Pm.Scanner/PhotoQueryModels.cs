namespace Pm.Scanner;

public sealed record PhotoListItem(long Id, string FileHash, int? Width, int? Height, string? Mime);
public sealed record PhotoPage(IReadOnlyList<PhotoListItem> Items, long? NextCursor);
