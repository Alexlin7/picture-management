using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Scanner.Tests;

public class MtimeSchemaTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-mtime-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public MtimeSchemaTests()
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
    public async Task Location_round_trips_mtime()
    {
        var when = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);

        await using (var ctx = NewContext())
        {
            var root = new LibraryRoot { Name = "本機", AbsPath = @"D:\pics" };
            var photo = new Photo { FileHash = new string('a', 64), FileSize = 10 };
            photo.Locations.Add(new PhotoLocation { LibraryRoot = root, RelPath = "a.png", Mtime = when });
            ctx.Photos.Add(photo);
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        var loc = await ctx2.PhotoLocations.SingleAsync();
        Assert.Equal(when, loc.Mtime);
    }
}
