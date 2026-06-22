using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pm.Data;
using Xunit;

namespace Pm.Data.Tests;

// 驗證 AddTagNameCi 的「升級保險」:既有 db 若有不分大小寫的重複 tag,
// migration 要先把它們合併(photo_tag 重指到 keeper)再建唯一索引,而非啟動中止。
public class TagNameCiMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pm-mig-{Guid.NewGuid():N}.sqlite");
    private string Cs => $"Data Source={_dbPath};Foreign Keys=True";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(Cs).Options);

    private static async Task Exec(SqliteConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Migration_merges_ci_duplicate_tags_then_builds_unique_index()
    {
        // 1) 先只 migrate 到 AddTagNameCi 的「前一個」(此時 tag 表無 name_ci、無 CI 唯一索引)。
        using (var ctx = NewContext())
            ctx.GetService<IMigrator>().Migrate("20260621144827_AddLocationMtime");

        // 2) 用原生 SQL 塞「Blue / blue」兩顆 CI 重複 tag,各掛到同一張 photo。
        //    (此時 entity 的 NameCi 欄尚不存在於 DB,故不能用 EF。)
        await using (var raw = new SqliteConnection(Cs))
        {
            await raw.OpenAsync();
            await Exec(raw, $"INSERT INTO photo(file_hash, file_size) VALUES ('{new string('a', 64)}', 1);");
            await Exec(raw, "INSERT INTO tag(name, kind) VALUES ('Blue', 'manual');");   // id=1 → keeper
            await Exec(raw, "INSERT INTO tag(name, kind) VALUES ('blue', 'general');");  // id=2 → dup
            await Exec(raw, "INSERT INTO photo_tag(photo_id, tag_id, source) VALUES (1, 1, 'manual');");
            await Exec(raw, "INSERT INTO photo_tag(photo_id, tag_id, source) VALUES (1, 2, 'wd14');");
        }

        // 3) migrate 到最新:跑 AddTagNameCi(回填 name_ci → 合併 CI 重複 → 建唯一索引)。
        //    若保險 SQL 沒先合併,建唯一索引會在 'blue'/'blue' 上拋,這行就會炸。
        using (var ctx = NewContext())
            ctx.Database.Migrate();

        // 4) 驗證:只剩 keeper 一顆,photo_tag 重指到它且不重複。
        await using var v = NewContext();
        var keeper = await v.Tags.SingleAsync();
        Assert.Equal("Blue", keeper.Name);            // keeper = MIN(id),拼寫保留
        Assert.Equal("blue", keeper.NameCi);          // name_ci 已回填
        Assert.Equal(1, await v.PhotoTags.CountAsync(pt => pt.TagId == keeper.Id));   // 兩筆併為一筆
    }
}
