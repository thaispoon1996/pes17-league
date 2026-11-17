using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MatchNews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchArticles",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FixtureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ByAi = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchArticles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchArticles_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalSchema: "clubs",
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchPhotos",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Caption = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPhotos_MatchArticles_ArticleId",
                        column: x => x.ArticleId,
                        principalSchema: "clubs",
                        principalTable: "MatchArticles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchArticles_FixtureId",
                schema: "clubs",
                table: "MatchArticles",
                column: "FixtureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchArticles_PublishedAt",
                schema: "clubs",
                table: "MatchArticles",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPhotos_ArticleId_Sort",
                schema: "clubs",
                table: "MatchPhotos",
                columns: new[] { "ArticleId", "Sort" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchPhotos",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "MatchArticles",
                schema: "clubs");
        }
    }
}
