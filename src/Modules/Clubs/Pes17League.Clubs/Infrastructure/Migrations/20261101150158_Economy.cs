using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Economy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MatchdayId",
                schema: "clubs",
                table: "TransferWindows",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContractMatches",
                schema: "clubs",
                table: "Transfers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Wage",
                schema: "clubs",
                table: "Transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayerPrices",
                schema: "clubs",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Overall = table.Column<int>(type: "integer", nullable: false),
                    BasePrice = table.Column<long>(type: "bigint", nullable: false),
                    FormMultiplier = table.Column<double>(type: "double precision", nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerPrices", x => new { x.PlayerId, x.SeasonId });
                    table.ForeignKey(
                        name: "FK_PlayerPrices_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoundSettlements",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchdayId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundSettlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeasonEconomies",
                schema: "clubs",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftWageCap = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonEconomies", x => x.SeasonId);
                    table.CheckConstraint("CK_SeasonEconomy_SoftWageCap", "\"SoftWageCap\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferWindows_MatchdayId",
                schema: "clubs",
                table: "TransferWindows",
                column: "MatchdayId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlayerContract_MatchesTotal",
                schema: "clubs",
                table: "Contracts",
                sql: "\"MatchesTotal\" IS NULL OR \"MatchesTotal\" BETWEEN 10 AND 60");

            migrationBuilder.CreateIndex(
                name: "IX_RoundSettlements_SeasonId_MatchdayId",
                schema: "clubs",
                table: "RoundSettlements",
                columns: new[] { "SeasonId", "MatchdayId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerPrices",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "RoundSettlements",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "SeasonEconomies",
                schema: "clubs");

            migrationBuilder.DropIndex(
                name: "IX_TransferWindows_MatchdayId",
                schema: "clubs",
                table: "TransferWindows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlayerContract_MatchesTotal",
                schema: "clubs",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "MatchdayId",
                schema: "clubs",
                table: "TransferWindows");

            migrationBuilder.DropColumn(
                name: "ContractMatches",
                schema: "clubs",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "Wage",
                schema: "clubs",
                table: "Transfers");
        }
    }
}
