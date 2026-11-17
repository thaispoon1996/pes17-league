using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinanceCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClubFinances",
                schema: "clubs",
                columns: table => new
                {
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    Fans = table.Column<int>(type: "integer", nullable: false),
                    TicketPrice = table.Column<long>(type: "bigint", nullable: false),
                    SponsorPerRound = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubFinances", x => x.ClubId);
                    table.ForeignKey(
                        name: "FK_ClubFinances_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerConditions",
                schema: "clubs",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fatigue = table.Column<int>(type: "integer", nullable: false),
                    Morale = table.Column<int>(type: "integer", nullable: false),
                    InjuredRounds = table.Column<int>(type: "integer", nullable: false),
                    Injury = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    LastMatchdayId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerConditions", x => x.PlayerId);
                    table.ForeignKey(
                        name: "FK_PlayerConditions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubFinances",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "PlayerConditions",
                schema: "clubs");
        }
    }
}
