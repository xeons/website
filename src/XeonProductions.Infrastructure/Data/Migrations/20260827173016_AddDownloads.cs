using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XeonProductions.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "downloads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    FileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresAuthentication = table.Column<bool>(type: "boolean", nullable: false),
                    ProtectionOverride = table.Column<int>(type: "integer", nullable: true),
                    AllowedReferrers = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DownloadCount = table.Column<long>(type: "bigint", nullable: false),
                    BlockedCount = table.Column<long>(type: "bigint", nullable: false),
                    LastDownloadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UploadedById = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_downloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_downloads_users_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_downloads_Slug",
                table: "downloads",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_downloads_UploadedById",
                table: "downloads",
                column: "UploadedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "downloads");
        }
    }
}
