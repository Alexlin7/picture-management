using Microsoft.EntityFrameworkCore;
using Pm.Data;

namespace Pm.Scanner;

public sealed class TagClosureService(PmDbContext db)
{
    /// <summary>tag 自身 + 所有後代(DAG,沿 tag_relation parent→child)。應用層保證無環。</summary>
    public async Task<List<long>> DescendantsAsync(long tagId, CancellationToken ct = default) =>
        await db.Database.SqlQuery<long>($@"
            WITH RECURSIVE descendants(id) AS (
                SELECT {tagId}
                UNION
                SELECT tr.child_tag_id
                FROM tag_relation tr
                JOIN descendants d ON tr.parent_tag_id = d.id
            )
            SELECT id AS ""Value"" FROM descendants").ToListAsync(ct);
}
