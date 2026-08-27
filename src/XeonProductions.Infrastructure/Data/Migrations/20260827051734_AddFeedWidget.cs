using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XeonProductions.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedWidget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeedUrl",
                table: "widgets",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowFeedDates",
                table: "widgets",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeedUrl",
                table: "widgets");

            migrationBuilder.DropColumn(
                name: "ShowFeedDates",
                table: "widgets");
        }
    }
}
