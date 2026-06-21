namespace Pm.Data.Entities;

public class TaggingJob
{
    public long PhotoId { get; set; }                 // 同時是 PK 與 FK→photo
    public string State { get; set; } = "pending";    // pending/running/done/error
    public int Attempts { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
