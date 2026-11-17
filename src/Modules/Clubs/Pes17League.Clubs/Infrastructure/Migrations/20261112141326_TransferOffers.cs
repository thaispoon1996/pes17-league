using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TransferOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Instalments",
                schema: "clubs",
                table: "Transfers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InstalmentsPaid",
                schema: "clubs",
                table: "Transfers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "OfferId",
                schema: "clubs",
                table: "Transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SellOnPercent",
                schema: "clubs",
                table: "Transfers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "LoanFromClubId",
                schema: "clubs",
                table: "Contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoanParentMatchesLeft",
                schema: "clubs",
                table: "Contracts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LoanParentWage",
                schema: "clubs",
                table: "Contracts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReleaseClause",
                schema: "clubs",
                table: "Contracts",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TransferOffers",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferWindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Fee = table.Column<long>(type: "bigint", nullable: false),
                    CounterFee = table.Column<long>(type: "bigint", nullable: true),
                    SwapPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SellOnPercent = table.Column<int>(type: "integer", nullable: false),
                    Instalments = table.Column<int>(type: "integer", nullable: false),
                    ContractMatches = table.Column<int>(type: "integer", nullable: false),
                    Wage = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FailReason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOffers", x => x.Id);
                    table.CheckConstraint("CK_TransferOffer_Fee", "\"Fee\" >= 0");
                    table.ForeignKey(
                        name: "FK_TransferOffers_Clubs_BuyerClubId",
                        column: x => x.BuyerClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransferOffers_Clubs_SellerClubId",
                        column: x => x.SellerClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransferOffers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransferOffers_TransferWindows_TransferWindowId",
                        column: x => x.TransferWindowId,
                        principalSchema: "clubs",
                        principalTable: "TransferWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOffers_BuyerClubId_Status",
                schema: "clubs",
                table: "TransferOffers",
                columns: new[] { "BuyerClubId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOffers_PlayerId",
                schema: "clubs",
                table: "TransferOffers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOffers_SellerClubId_Status",
                schema: "clubs",
                table: "TransferOffers",
                columns: new[] { "SellerClubId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOffers_TransferWindowId",
                schema: "clubs",
                table: "TransferOffers",
                column: "TransferWindowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransferOffers",
                schema: "clubs");

            migrationBuilder.DropColumn(
                name: "Instalments",
                schema: "clubs",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "InstalmentsPaid",
                schema: "clubs",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "OfferId",
                schema: "clubs",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "SellOnPercent",
                schema: "clubs",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "LoanFromClubId",
                schema: "clubs",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LoanParentMatchesLeft",
                schema: "clubs",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LoanParentWage",
                schema: "clubs",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ReleaseClause",
                schema: "clubs",
                table: "Contracts");
        }
    }
}
