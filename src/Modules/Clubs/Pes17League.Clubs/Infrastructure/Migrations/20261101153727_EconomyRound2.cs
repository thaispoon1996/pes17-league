using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EconomyRound2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BudgetExponent",
                schema: "clubs",
                table: "SeasonEconomies",
                type: "double precision",
                nullable: false,
                defaultValue: 0.5);

            migrationBuilder.AddColumn<int>(
                name: "DefaultContractMatches",
                schema: "clubs",
                table: "SeasonEconomies",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<long>(
                name: "InitialBudgetFund",
                schema: "clubs",
                table: "SeasonEconomies",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "WagePermille",
                schema: "clubs",
                table: "SeasonEconomies",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginClubId",
                schema: "clubs",
                table: "Players",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginPesClubIndex",
                schema: "clubs",
                table: "PlayerPesMappings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndReason",
                schema: "clubs",
                table: "Contracts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractRenewals",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    Matches = table.Column<int>(type: "integer", nullable: false),
                    Fee = table.Column<long>(type: "bigint", nullable: false),
                    Wage = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AutoApproved = table.Column<bool>(type: "boolean", nullable: false),
                    RejectReason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractRenewals", x => x.Id);
                    table.CheckConstraint("CK_ContractRenewal_Fee", "\"Fee\" >= 0");
                    table.CheckConstraint("CK_ContractRenewal_Matches", "\"Matches\" BETWEEN 10 AND 60");
                    table.ForeignKey(
                        name: "FK_ContractRenewals_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractRenewals_Clubs_OriginClubId",
                        column: x => x.OriginClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContractRenewals_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "clubs",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractRenewals_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SeasonEconomy_DefaultContractMatches",
                schema: "clubs",
                table: "SeasonEconomies",
                sql: "\"DefaultContractMatches\" BETWEEN 10 AND 60");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SeasonEconomy_InitialBudgetFund",
                schema: "clubs",
                table: "SeasonEconomies",
                sql: "\"InitialBudgetFund\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SeasonEconomy_WagePermille",
                schema: "clubs",
                table: "SeasonEconomies",
                sql: "\"WagePermille\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Players_OriginClubId",
                schema: "clubs",
                table: "Players",
                column: "OriginClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractRenewals_ClubId",
                schema: "clubs",
                table: "ContractRenewals",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractRenewals_ContractId",
                schema: "clubs",
                table: "ContractRenewals",
                column: "ContractId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_ContractRenewals_OriginClubId",
                schema: "clubs",
                table: "ContractRenewals",
                column: "OriginClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractRenewals_PlayerId",
                schema: "clubs",
                table: "ContractRenewals",
                column: "PlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Clubs_OriginClubId",
                schema: "clubs",
                table: "Players",
                column: "OriginClubId",
                principalSchema: "clubs",
                principalTable: "Clubs",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Clubs_OriginClubId",
                schema: "clubs",
                table: "Players");

            migrationBuilder.DropTable(
                name: "ContractRenewals",
                schema: "clubs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SeasonEconomy_DefaultContractMatches",
                schema: "clubs",
                table: "SeasonEconomies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SeasonEconomy_InitialBudgetFund",
                schema: "clubs",
                table: "SeasonEconomies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SeasonEconomy_WagePermille",
                schema: "clubs",
                table: "SeasonEconomies");

            migrationBuilder.DropIndex(
                name: "IX_Players_OriginClubId",
                schema: "clubs",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "BudgetExponent",
                schema: "clubs",
                table: "SeasonEconomies");

            migrationBuilder.DropColumn(
                name: "DefaultContractMatches",
                schema: "clubs",
                table: "SeasonEconomies");

            migrationBuilder.DropColumn(
                name: "InitialBudgetFund",
                schema: "clubs",
                table: "SeasonEconomies");

            migrationBuilder.DropColumn(
                name: "WagePermille",
                schema: "clubs",
                table: "SeasonEconomies");

            migrationBuilder.DropColumn(
                name: "OriginClubId",
                schema: "clubs",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "OriginPesClubIndex",
                schema: "clubs",
                table: "PlayerPesMappings");

            migrationBuilder.DropColumn(
                name: "EndReason",
                schema: "clubs",
                table: "Contracts");
        }
    }
}
