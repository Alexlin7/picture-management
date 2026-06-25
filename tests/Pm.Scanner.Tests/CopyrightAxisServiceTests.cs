using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class CopyrightAxisServiceTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pm-cax-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_db};Foreign Keys=True";
    private PmDbContext NewContext() => new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    public CopyrightAxisServiceTests()
    {
        using var ctx = NewContext();
        ctx.Database.Migrate();
    }

    private CopyrightAxisService Svc(PmDbContext ctx) => new(ctx, new TagService(ctx), new TagClosureService(ctx));

    [Fact]
    public async Task Seeds_copyright_tag_and_edge_from_character()
    {
        using var ctx = NewContext();
        var character = await new TagService(ctx).UpsertByNameAsync("aris_(blue_archive)", "character");

        var added = await Svc(ctx).SeedFromCharacterAsync(character);

        Assert.True(added);
        var copyright = await ctx.Tags.FirstAsync(t => t.Name == "blue_archive");
        Assert.Equal("copyright", copyright.Kind);
        Assert.True(await ctx.TagRelations.AnyAsync(r => r.ParentTagId == copyright.Id && r.ChildTagId == character.Id));
    }

    [Fact]
    public async Task Is_idempotent_no_duplicate_edge()
    {
        using var ctx = NewContext();
        var character = await new TagService(ctx).UpsertByNameAsync("aris_(blue_archive)", "character");
        await Svc(ctx).SeedFromCharacterAsync(character);

        var addedAgain = await Svc(ctx).SeedFromCharacterAsync(character);

        Assert.False(addedAgain);
        Assert.Equal(1, await ctx.TagRelations.CountAsync());
    }

    [Fact]
    public async Task No_work_no_edge()
    {
        using var ctx = NewContext();
        var character = await new TagService(ctx).UpsertByNameAsync("long_hair", "character");

        Assert.False(await Svc(ctx).SeedFromCharacterAsync(character));
        Assert.Equal(0, await ctx.TagRelations.CountAsync());
    }

    [Fact]
    public async Task Non_character_kind_ignored()
    {
        using var ctx = NewContext();
        var general = await new TagService(ctx).UpsertByNameAsync("blue_archive", "general");

        Assert.False(await Svc(ctx).SeedFromCharacterAsync(general));
        Assert.Equal(0, await ctx.TagRelations.CountAsync());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
