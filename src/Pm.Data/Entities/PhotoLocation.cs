namespace Pm.Data.Entities;

public class PhotoLocation
{
    public long Id { get; set; }
    public long PhotoId { get; set; }
    public Photo Photo { get; set; } = null!;
    public long LibraryRootId { get; set; }
    public LibraryRoot LibraryRoot { get; set; } = null!;
    public string RelPath { get; set; } = null!;
    public string Status { get; set; } = "present";   // present/missing/archived
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
