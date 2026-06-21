namespace Pm.Data.Entities;

public class LibraryRoot
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string AbsPath { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public List<PhotoLocation> Locations { get; } = new();
}
