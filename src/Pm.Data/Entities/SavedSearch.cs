namespace Pm.Data.Entities;

public class SavedSearch
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string QueryJson { get; set; } = null!;   // JSON 存 TEXT
    public DateTimeOffset CreatedAt { get; set; }
}
