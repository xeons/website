using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XeonProductions.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPageViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "page_views",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ViewId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ViewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VisitorHash = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReferrerHost = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ReferrerUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Browser = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    OperatingSystem = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Device = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsEntry = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_page_views", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_page_views_SessionId",
                table: "page_views",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_page_views_ViewedAt",
                table: "page_views",
                column: "ViewedAt");

            migrationBuilder.CreateIndex(
                name: "IX_page_views_ViewedAt_Path",
                table: "page_views",
                columns: new[] { "ViewedAt", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_page_views_ViewId",
                table: "page_views",
                column: "ViewId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page_views");
        }
    }
}
