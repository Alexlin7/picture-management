using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pm.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationMtime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mtime",
                table: "photo_location",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mtime",
                table: "photo_location");
        }
    }
}
