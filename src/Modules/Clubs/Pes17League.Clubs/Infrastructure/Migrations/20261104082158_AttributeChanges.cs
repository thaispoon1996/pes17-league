using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttributeChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttributeChanges",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FixtureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Field = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttributeChanges_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalSchema: "clubs",
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttributeChanges_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttributeChanges_FixtureId",
                schema: "clubs",
                table: "AttributeChanges",
                column: "FixtureId");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeChanges_PlayerId",
                schema: "clubs",
                table: "AttributeChanges",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeChanges_SeasonId_PlayerId",
                schema: "clubs",
                table: "AttributeChanges",
                columns: new[] { "SeasonId", "PlayerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttributeChanges",
                schema: "clubs");
        }
    }
}
