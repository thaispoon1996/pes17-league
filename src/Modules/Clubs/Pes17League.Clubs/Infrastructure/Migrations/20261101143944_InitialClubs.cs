using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialClubs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "clubs");

            migrationBuilder.CreateTable(
                name: "Clubs",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SquadVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatchVersions",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BaseOptionFileSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BaseOptionFilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatchVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Position = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    BirthYear = table.Column<int>(type: "integer", nullable: true),
                    Nationality = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransferWindows",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpensAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosesAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferWindows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ledger",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RefType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    RefId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ledger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ledger_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamAssignments",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ControllerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    From = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    To = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamAssignments_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClubPesMappings",
                schema: "clubs",
                columns: table => new
                {
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatchVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PesClubIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubPesMappings", x => new { x.ClubId, x.PatchVersionId });
                    table.ForeignKey(
                        name: "FK_ClubPesMappings_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClubPesMappings_PatchVersions_PatchVersionId",
                        column: x => x.PatchVersionId,
                        principalSchema: "clubs",
                        principalTable: "PatchVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OptionFileBuilds",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatchVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchdayId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BuiltAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SquadSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    Errors = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptionFileBuilds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OptionFileBuilds_PatchVersions_PatchVersionId",
                        column: x => x.PatchVersionId,
                        principalSchema: "clubs",
                        principalTable: "PatchVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchesTotal = table.Column<int>(type: "integer", nullable: true),
                    MatchesPlayed = table.Column<int>(type: "integer", nullable: false),
                    Wage = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contracts_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Contracts_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerAttributes",
                schema: "clubs",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Values = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAttributes", x => new { x.PlayerId, x.SeasonId });
                    table.ForeignKey(
                        name: "FK_PlayerAttributes_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerPesMappings",
                schema: "clubs",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatchVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PesPlayerId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerPesMappings", x => new { x.PlayerId, x.PatchVersionId });
                    table.ForeignKey(
                        name: "FK_PlayerPesMappings_PatchVersions_PatchVersionId",
                        column: x => x.PatchVersionId,
                        principalSchema: "clubs",
                        principalTable: "PatchVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerPesMappings_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SquadMemberships",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShirtNumber = table.Column<int>(type: "integer", nullable: false),
                    SlotOrder = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquadMemberships", x => x.Id);
                    table.CheckConstraint("CK_SquadMembership_ShirtNumber", "\"ShirtNumber\" BETWEEN 1 AND 99");
                    table.CheckConstraint("CK_SquadMembership_SlotOrder", "\"SlotOrder\" BETWEEN 0 AND 31");
                    table.ForeignKey(
                        name: "FK_SquadMemberships_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SquadMemberships_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transfers",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferWindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fee = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RejectReason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResultingLineupReview = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.CheckConstraint("CK_Transfer_Fee", "\"Fee\" >= 0");
                    table.ForeignKey(
                        name: "FK_Transfers_Clubs_FromClubId",
                        column: x => x.FromClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transfers_Clubs_ToClubId",
                        column: x => x.ToClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transfers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Transfers_TransferWindows_TransferWindowId",
                        column: x => x.TransferWindowId,
                        principalSchema: "clubs",
                        principalTable: "TransferWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubPesMappings_PatchVersionId_PesClubIndex",
                schema: "clubs",
                table: "ClubPesMappings",
                columns: new[] { "PatchVersionId", "PesClubIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ClubId",
                schema: "clubs",
                table: "Contracts",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_PlayerId",
                schema: "clubs",
                table: "Contracts",
                column: "PlayerId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_Ledger_ClubId",
                schema: "clubs",
                table: "Ledger",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_OptionFileBuilds_MatchdayId",
                schema: "clubs",
                table: "OptionFileBuilds",
                column: "MatchdayId");

            migrationBuilder.CreateIndex(
                name: "IX_OptionFileBuilds_PatchVersionId",
                schema: "clubs",
                table: "OptionFileBuilds",
                column: "PatchVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchVersions_Name",
                schema: "clubs",
                table: "PatchVersions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerPesMappings_PatchVersionId_PesPlayerId",
                schema: "clubs",
                table: "PlayerPesMappings",
                columns: new[] { "PatchVersionId", "PesPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SquadMemberships_ClubId_ShirtNumber",
                schema: "clubs",
                table: "SquadMemberships",
                columns: new[] { "ClubId", "ShirtNumber" },
                unique: true,
                filter: "\"LeftAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SquadMemberships_ClubId_SlotOrder",
                schema: "clubs",
                table: "SquadMemberships",
                columns: new[] { "ClubId", "SlotOrder" },
                filter: "\"LeftAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SquadMemberships_PlayerId",
                schema: "clubs",
                table: "SquadMemberships",
                column: "PlayerId",
                unique: true,
                filter: "\"LeftAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamAssignments_ClubId",
                schema: "clubs",
                table: "TeamAssignments",
                column: "ClubId",
                unique: true,
                filter: "\"To\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_FromClubId",
                schema: "clubs",
                table: "Transfers",
                column: "FromClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_PlayerId",
                schema: "clubs",
                table: "Transfers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ToClubId",
                schema: "clubs",
                table: "Transfers",
                column: "ToClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TransferWindowId",
                schema: "clubs",
                table: "Transfers",
                column: "TransferWindowId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferWindows_SeasonId",
                schema: "clubs",
                table: "TransferWindows",
                column: "SeasonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubPesMappings",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Contracts",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Ledger",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "OptionFileBuilds",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "PlayerAttributes",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "PlayerPesMappings",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "SquadMemberships",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "TeamAssignments",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Transfers",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "PatchVersions",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Clubs",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Players",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "TransferWindows",
                schema: "clubs");
        }
    }
}
