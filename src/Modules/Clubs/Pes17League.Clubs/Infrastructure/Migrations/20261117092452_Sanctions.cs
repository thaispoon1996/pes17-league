using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sanctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sanctions",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    EditionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RefFixtureId = table.Column<Guid>(type: "uuid", nullable: true),
                    BannedFixtureIds = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sanctions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sanctions_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Sanctions_Editions_EditionId",
                        column: x => x.EditionId,
                        principalSchema: "clubs",
                        principalTable: "Editions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Sanctions_Managers_ManagerId",
                        column: x => x.ManagerId,
                        principalSchema: "clubs",
                        principalTable: "Managers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Sanctions_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "clubs",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sanctions_ClubId",
                schema: "clubs",
                table: "Sanctions",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Sanctions_EditionId",
                schema: "clubs",
                table: "Sanctions",
                column: "EditionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sanctions_ManagerId",
                schema: "clubs",
                table: "Sanctions",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sanctions_SeasonId",
                schema: "clubs",
                table: "Sanctions",
                column: "SeasonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sanctions",
                schema: "clubs");
        }
    }
}
