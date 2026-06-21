using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Data.Tests;

public class SchemaTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-test-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public SchemaTests()
    {
        using var ctx = NewContext();
        ctx.Database.Migrate();   // 套 migration 建出 schema
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();   // 釋放檔案 handle 才刪得掉
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private PmDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<PmDbContext>()
            .UseSqlite(Cs)
            .Options;
        return new PmDbContext(options);
    }

    [Fact]
    public async Task Round_trip_photo_with_location()
    {
        await using var ctx = NewContext();

        var root = new LibraryRoot { Name = "本機", AbsPath = @"D:\pics" };
        ctx.LibraryRoots.Add(root);
        await ctx.SaveChangesAsync();

        var photo = new Photo { FileHash = new string('a', 64), FileSize = 1234, Mime = "image/png" };
        photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = "vspo/sample.png" });
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        var loaded = await ctx2.Photos
            .Include(p => p.Locations)
            .SingleAsync(p => p.FileHash == new string('a', 64));

        Assert.Equal(1234, loaded.FileSize);
        Assert.Single(loaded.Locations);
        Assert.Equal("present", loaded.Locations[0].Status);   // 預設值生效
    }

    [Fact]
    public async Task Duplicate_file_hash_is_rejected()
    {
        var hash = new string('b', 64);

        await using (var ctx = NewContext())
        {
            ctx.Photos.Add(new Photo { FileHash = hash });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        ctx2.Photos.Add(new Photo { FileHash = hash });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    [Fact]
    public async Task Tag_relation_self_reference_is_rejected()
    {
        long tagId;
        await using (var ctx = NewContext())
        {
            var t = new Tag { Name = "vspo", Kind = "copyright" };
            ctx.Tags.Add(t);
            await ctx.SaveChangesAsync();
            tagId = t.Id;
        }

        // parent == child 應觸發 ck_tagrel_no_self
        await using var raw = new SqliteConnection(Cs);
        await raw.OpenAsync();
        await using var cmd = raw.CreateCommand();
        cmd.CommandText = "INSERT INTO tag_relation(parent_tag_id, child_tag_id) VALUES ($id, $id)";
        cmd.Parameters.AddWithValue("$id", tagId);

        await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
    }
}
