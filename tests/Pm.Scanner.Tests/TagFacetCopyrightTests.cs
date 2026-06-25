using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Pm.Scanner;
using Xunit;

namespace Pm.Scanner.Tests;

public class TagFacetCopyrightTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pm-facet-{Guid.NewGuid():N}.sqlite");
    private PmDbContext NewContext() => new(new DbContextOptionsBuilder<PmDbContext>()
        .UseSqlite($"Data Source={_db};Foreign Keys=True").Options);

    public TagFacetCopyrightTests() { using var c = NewContext(); c.Database.Migrate(); }

    [Fact]
    public async Task Tree_only_copyright_and_character_general_excluded()
    {
        using var ctx = NewContext();
        // 作品 → 角色 + 一個 present photo 掛角色;外加一個 general tag(不該進 tree/rootless)
        var root = new LibraryRoot { Name = "r", AbsPath = @"C:\x" }; ctx.LibraryRoots.Add(root);
        var copyright = new Tag { Name = "blue_archive", Kind = "copyright" };
        var character = new Tag { Name = "aris_(blue_archive)", Kind = "character" };
        var general = new Tag { Name = "long_hair", Kind = "general" };
        ctx.Tags.AddRange(copyright, character, general);
        var photo = new Photo { FileHash = new string('a', 64), ImportedAt = DateTimeOffset.UtcNow };
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
        ctx.PhotoLocations.Add(new PhotoLocation { PhotoId = photo.Id, LibraryRootId = root.Id, RelPath = "a.png", Status = "present", FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow });
        ctx.PhotoTags.Add(new PhotoTag { PhotoId = photo.Id, TagId = character.Id, Source = "wd14" });
        ctx.PhotoTags.Add(new PhotoTag { PhotoId = photo.Id, TagId = general.Id, Source = "wd14" });
        ctx.TagRelations.Add(new TagRelation { ParentTagId = copyright.Id, ChildTagId = character.Id });
        await ctx.SaveChangesAsync();

        var tree = await new TagFacetService(ctx).BuildAsync();

        // 樹頂只有 copyright,其下為 character;general 不在 tree/rootless
        var top = Assert.Single(tree.Tree);
        Assert.Equal("blue_archive", top.Name);
        Assert.Equal("copyright", top.Kind);
        Assert.Equal(1, top.Count);                       // copyright 聚合 = 子角色 count 總和
        var child = Assert.Single(top.Children!);
        Assert.Equal("aris_(blue_archive)", child.Name);
        Assert.DoesNotContain(tree.Rootless, n => n.Kind == "general");
        Assert.DoesNotContain(tree.Tree, n => n.Kind == "general");
        Assert.Contains(tree.General, g => g.Name == "long_hair");   // general 仍在專屬區
    }

    [Fact]
    public async Task Character_without_copyright_goes_rootless()
    {
        using var ctx = NewContext();
        ctx.Tags.Add(new Tag { Name = "solo_character", Kind = "character" });
        await ctx.SaveChangesAsync();

        var tree = await new TagFacetService(ctx).BuildAsync();

        Assert.Contains(tree.Rootless, n => n.Name == "solo_character" && n.Kind == "character");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
