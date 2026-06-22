using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class TagClosureTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-closure-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public TagClosureTests()
    {
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    [Fact]
    public async Task Descendants_includes_self_children_and_grandchildren()
    {
        long agency, project, chara;
        await using (var ctx = NewContext())
        {
            var a = new Tag { Name = "2434", Kind = "copyright" };       // 企劃
            var p = new Tag { Name = "vspo", Kind = "copyright" };       // 作品
            var c = new Tag { Name = "tokino_sora", Kind = "character" };// 角色
            ctx.Tags.AddRange(a, p, c);
            await ctx.SaveChangesAsync();
            ctx.TagRelations.Add(new TagRelation { ParentTagId = a.Id, ChildTagId = p.Id });
            ctx.TagRelations.Add(new TagRelation { ParentTagId = p.Id, ChildTagId = c.Id });
            await ctx.SaveChangesAsync();
            agency = a.Id; project = p.Id; chara = c.Id;
        }

        await using var ctx2 = NewContext();
        var closure = new TagClosureService(ctx2);

        var top = await closure.DescendantsAsync(agency);
        Assert.Equal(new[] { agency, project, chara }.OrderBy(x => x), top.OrderBy(x => x));

        var leaf = await closure.DescendantsAsync(chara);
        Assert.Equal(new[] { chara }, leaf);   // 葉只有自己
    }

    [Fact]
    public async Task Multi_parent_node_appears_under_each_parent_closure()
    {
        long p1, p2, shared;
        await using (var ctx = NewContext())
        {
            var a = new Tag { Name = "projA", Kind = "copyright" };
            var b = new Tag { Name = "projB", Kind = "copyright" };
            var s = new Tag { Name = "collab_unit", Kind = "character" };
            ctx.Tags.AddRange(a, b, s);
            await ctx.SaveChangesAsync();
            ctx.TagRelations.Add(new TagRelation { ParentTagId = a.Id, ChildTagId = s.Id });
            ctx.TagRelations.Add(new TagRelation { ParentTagId = b.Id, ChildTagId = s.Id });
            await ctx.SaveChangesAsync();
            p1 = a.Id; p2 = b.Id; shared = s.Id;
        }

        await using var ctx2 = NewContext();
        var closure = new TagClosureService(ctx2);
        Assert.Contains(shared, await closure.DescendantsAsync(p1));
        Assert.Contains(shared, await closure.DescendantsAsync(p2));
    }
}
