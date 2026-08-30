using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XeonProductions.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaVariantWidths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "VariantWidths",
                table: "media",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VariantWidths",
                table: "media");
        }
    }
}
