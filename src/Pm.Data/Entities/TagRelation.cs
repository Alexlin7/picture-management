namespace Pm.Data.Entities;

// DAG 邊:一個 tag 可有 0/1/多個上層;不知上游=無此邊,留最上層。
public class TagRelation
{
    public long ParentTagId { get; set; }
    public long ChildTagId { get; set; }
}
