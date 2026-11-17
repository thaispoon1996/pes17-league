using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Competition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Seasons",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    PatchVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_PatchVersions_PatchVersionId",
                        column: x => x.PatchVersionId,
                        principalSchema: "clubs",
                        principalTable: "PatchVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Matchdays",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TransferWindowId = table.Column<Guid>(type: "uuid", nullable: true),
                    OptionFileBuildId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matchdays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matchdays_OptionFileBuilds_OptionFileBuildId",
                        column: x => x.OptionFileBuildId,
                        principalSchema: "clubs",
                        principalTable: "OptionFileBuilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Matchdays_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "clubs",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Matchdays_TransferWindows_TransferWindowId",
                        column: x => x.TransferWindowId,
                        principalSchema: "clubs",
                        principalTable: "TransferWindows",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SeasonClubs",
                schema: "clubs",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonClubs", x => new { x.SeasonId, x.ClubId });
                    table.ForeignKey(
                        name: "FK_SeasonClubs_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeasonClubs_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "clubs",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Fixtures",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchdayId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HomeGoals = table.Column<int>(type: "integer", nullable: true),
                    AwayGoals = table.Column<int>(type: "integer", nullable: true),
                    ResultSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PlayedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fixtures", x => x.Id);
                    table.CheckConstraint("CK_Fixture_Clubs", "\"HomeClubId\" <> \"AwayClubId\"");
                    table.CheckConstraint("CK_Fixture_Goals", "(\"HomeGoals\" IS NULL OR \"HomeGoals\" >= 0) AND (\"AwayGoals\" IS NULL OR \"AwayGoals\" >= 0)");
                    table.ForeignKey(
                        name: "FK_Fixtures_Clubs_AwayClubId",
                        column: x => x.AwayClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Fixtures_Clubs_HomeClubId",
                        column: x => x.HomeClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Fixtures_Matchdays_MatchdayId",
                        column: x => x.MatchdayId,
                        principalSchema: "clubs",
                        principalTable: "Matchdays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Goals",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FixtureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Half = table.Column<int>(type: "integer", nullable: false),
                    Minute = table.Column<int>(type: "integer", nullable: false),
                    VideoOffsetSec = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Goals_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Goals_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalSchema: "clubs",
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchAttempts",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FixtureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContainerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RecordingPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchAttempts_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalSchema: "clubs",
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_AwayClubId",
                schema: "clubs",
                table: "Fixtures",
                column: "AwayClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_HomeClubId",
                schema: "clubs",
                table: "Fixtures",
                column: "HomeClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_MatchdayId",
                schema: "clubs",
                table: "Fixtures",
                column: "MatchdayId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_ClubId",
                schema: "clubs",
                table: "Goals",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_FixtureId",
                schema: "clubs",
                table: "Goals",
                column: "FixtureId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchAttempts_FixtureId",
                schema: "clubs",
                table: "MatchAttempts",
                column: "FixtureId",
                unique: true,
                filter: "\"Outcome\" = 'Running'");

            migrationBuilder.CreateIndex(
                name: "IX_Matchdays_OptionFileBuildId",
                schema: "clubs",
                table: "Matchdays",
                column: "OptionFileBuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Matchdays_SeasonId_Number",
                schema: "clubs",
                table: "Matchdays",
                columns: new[] { "SeasonId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matchdays_TransferWindowId",
                schema: "clubs",
                table: "Matchdays",
                column: "TransferWindowId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonClubs_ClubId",
                schema: "clubs",
                table: "SeasonClubs",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_PatchVersionId",
                schema: "clubs",
                table: "Seasons",
                column: "PatchVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Goals",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "MatchAttempts",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "SeasonClubs",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Fixtures",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Matchdays",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Seasons",
                schema: "clubs");
        }
    }
}
