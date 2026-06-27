using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pm.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizePathRuleActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 正規化歷史 path_tag_rule.action 詞彙:前端曾送 map/year,但服務層只認
            // map_to_tag/meta_year,導致 map 規則靜默不建 tag(bug)。把舊值就地改成正規值,
            // 與服務層一致;TagId 仍為 null 的舊 map 規則由 ApplyExistingRulesAsync 自我修復補建。
            migrationBuilder.Sql("UPDATE path_tag_rule SET action = 'map_to_tag' WHERE action = 'map';");
            migrationBuilder.Sql("UPDATE path_tag_rule SET action = 'meta_year' WHERE action = 'year';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE path_tag_rule SET action = 'map' WHERE action = 'map_to_tag';");
            migrationBuilder.Sql("UPDATE path_tag_rule SET action = 'year' WHERE action = 'meta_year';");
        }
    }
}
