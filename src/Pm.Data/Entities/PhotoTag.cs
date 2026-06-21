namespace Pm.Data.Entities;

public class PhotoTag
{
    public long PhotoId { get; set; }
    public long TagId { get; set; }
    public string Source { get; set; } = null!;   // path/manual/wd14
    public float? Confidence { get; set; }
}
