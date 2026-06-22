using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pm.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "library_root",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    abs_path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_root", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "photo",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    file_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    file_size = table.Column<long>(type: "INTEGER", nullable: true),
                    width = table.Column<int>(type: "INTEGER", nullable: true),
                    height = table.Column<int>(type: "INTEGER", nullable: true),
                    mime = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    taken_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    camera_model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    gps_lat = table.Column<double>(type: "REAL", nullable: true),
                    gps_lon = table.Column<double>(type: "REAL", nullable: true),
                    exif = table.Column<string>(type: "TEXT", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_search",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    query_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_search", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tag",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "manual")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "photo_location",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    photo_id = table.Column<long>(type: "INTEGER", nullable: false),
                    library_root_id = table.Column<long>(type: "INTEGER", nullable: false),
                    rel_path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, defaultValue: "present"),
                    first_seen_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    last_seen_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photo_location", x => x.id);
                    table.ForeignKey(
                        name: "FK_photo_location_library_root_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_root",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_photo_location_photo_photo_id",
                        column: x => x.photo_id,
                        principalTable: "photo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tagging_job",
                columns: table => new
                {
                    photo_id = table.Column<long>(type: "INTEGER", nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, defaultValue: "pending"),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    enqueued_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tagging_job", x => x.photo_id);
                    table.ForeignKey(
                        name: "FK_tagging_job_photo_photo_id",
                        column: x => x.photo_id,
                        principalTable: "photo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "path_tag_rule",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    library_root_id = table.Column<long>(type: "INTEGER", nullable: true),
                    segment = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    action = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    tag_id = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_path_tag_rule", x => x.id);
                    table.ForeignKey(
                        name: "FK_path_tag_rule_library_root_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_root",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_path_tag_rule_tag_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tag",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "photo_tag",
                columns: table => new
                {
                    photo_id = table.Column<long>(type: "INTEGER", nullable: false),
                    tag_id = table.Column<long>(type: "INTEGER", nullable: false),
                    source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    confidence = table.Column<float>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photo_tag", x => new { x.photo_id, x.tag_id });
                    table.ForeignKey(
                        name: "FK_photo_tag_photo_photo_id",
                        column: x => x.photo_id,
                        principalTable: "photo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_photo_tag_tag_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tag",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tag_relation",
                columns: table => new
                {
                    parent_tag_id = table.Column<long>(type: "INTEGER", nullable: false),
                    child_tag_id = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_relation", x => new { x.parent_tag_id, x.child_tag_id });
                    table.CheckConstraint("ck_tagrel_no_self", "parent_tag_id <> child_tag_id");
                    table.ForeignKey(
                        name: "FK_tag_relation_tag_child_tag_id",
                        column: x => x.child_tag_id,
                        principalTable: "tag",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tag_relation_tag_parent_tag_id",
                        column: x => x.parent_tag_id,
                        principalTable: "tag",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_library_root_abs_path",
                table: "library_root",
                column: "abs_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_path_tag_rule_library_root_id_segment",
                table: "path_tag_rule",
                columns: new[] { "library_root_id", "segment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_path_tag_rule_tag_id",
                table: "path_tag_rule",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "IX_photo_file_hash",
                table: "photo",
                column: "file_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_photo_taken",
                table: "photo",
                column: "taken_at");

            migrationBuilder.CreateIndex(
                name: "ix_loc_photo",
                table: "photo_location",
                column: "photo_id");

            migrationBuilder.CreateIndex(
                name: "IX_photo_location_library_root_id_rel_path",
                table: "photo_location",
                columns: new[] { "library_root_id", "rel_path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_phototag_tag",
                table: "photo_tag",
                columns: new[] { "tag_id", "photo_id" });

            migrationBuilder.CreateIndex(
                name: "IX_tag_name",
                table: "tag",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tagrel_child",
                table: "tag_relation",
                column: "child_tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_state",
                table: "tagging_job",
                column: "state",
                filter: "state IN ('pending','error')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "path_tag_rule");

            migrationBuilder.DropTable(
                name: "photo_location");

            migrationBuilder.DropTable(
                name: "photo_tag");

            migrationBuilder.DropTable(
                name: "saved_search");

            migrationBuilder.DropTable(
                name: "tag_relation");

            migrationBuilder.DropTable(
                name: "tagging_job");

            migrationBuilder.DropTable(
                name: "library_root");

            migrationBuilder.DropTable(
                name: "tag");

            migrationBuilder.DropTable(
                name: "photo");
        }
    }
}
