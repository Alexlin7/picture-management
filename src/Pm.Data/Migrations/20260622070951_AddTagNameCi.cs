using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pm.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagNameCi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name_ci",
                table: "tag",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            // 回填既有列:SQLite lower() 僅折 ASCII(既有開發/WD14 資料皆 ASCII,正確)。
            // 之後新增/改名的列由 PmDbContext.SaveChanges 以 ToLowerInvariant 精確維護。
            // 必須在建唯一索引「之前」回填,否則多筆空字串會違反 ux_tag_name_ci。
            migrationBuilder.Sql("UPDATE tag SET name_ci = lower(name);");

            // 升級保險:合併「不分大小寫同名」的歷史重複 tag(例如先前手動建過 blue 又有 Blue),
            // 否則下面的唯一索引會在既有資料上建立失敗、啟動中止。
            // 每組保留 id 最小的(keeper,沿用其拼寫/kind),把其餘 dup 的 photo_tag 併到 keeper
            // (INSERT OR IGNORE:keeper 已掛該圖則略過,避免 (photo_id,tag_id) 主鍵衝突)。
            migrationBuilder.Sql(@"
INSERT OR IGNORE INTO photo_tag (photo_id, tag_id, source, confidence)
SELECT pt.photo_id,
       (SELECT MIN(t2.id) FROM tag t2 WHERE t2.name_ci = t1.name_ci),
       pt.source, pt.confidence
FROM photo_tag pt
JOIN tag t1 ON t1.id = pt.tag_id
WHERE t1.id <> (SELECT MIN(t2.id) FROM tag t2 WHERE t2.name_ci = t1.name_ci);");

            // 刪掉所有非 keeper 的 dup tag;FK cascade 會清掉其殘留 photo_tag / tag_relation。
            migrationBuilder.Sql(
                "DELETE FROM tag WHERE id NOT IN (SELECT MIN(id) FROM tag GROUP BY name_ci);");

            migrationBuilder.CreateIndex(
                name: "ux_tag_name_ci",
                table: "tag",
                column: "name_ci",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_tag_name_ci",
                table: "tag");

            migrationBuilder.DropColumn(
                name: "name_ci",
                table: "tag");
        }
    }
}
