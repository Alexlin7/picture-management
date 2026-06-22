namespace Pm.Data.Entities;

public class PathTagRule
{
    public long Id { get; set; }
    public long? LibraryRootId { get; set; }      // NULL = 全域
    public string Segment { get; set; } = null!;
    public string Action { get; set; } = null!;   // map_to_tag/ignore/meta_year
    public long? TagId { get; set; }
}
