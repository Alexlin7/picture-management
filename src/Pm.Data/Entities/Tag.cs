namespace Pm.Data.Entities;

public class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;          // booru 式全域唯一名
    public string Kind { get; set; } = "manual";       // path/manual/character/copyright/general/meta
}
