using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MatchStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Corners",
                schema: "clubs",
                table: "MatchRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Fouls",
                schema: "clubs",
                table: "MatchRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Goals",
                schema: "clubs",
                table: "MatchRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InMinute",
                schema: "clubs",
                table: "MatchRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Offsides",
                schema: "clubs",
                table: "MatchRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutMinute",
                schema: "clubs",
                table: "MatchRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RedCards",
                schema: "clubs",
                table: "MatchRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Shots",
                schema: "clubs",
                table: "MatchRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShotsOnTarget",
                schema: "clubs",
                table: "MatchRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YellowCards",
                schema: "clubs",
                table: "MatchRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MatchEvents",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FixtureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<int>(type: "integer", nullable: false),
                    Half = table.Column<int>(type: "integer", nullable: false),
                    Minute = table.Column<int>(type: "integer", nullable: false),
                    AddedTime = table.Column<bool>(type: "boolean", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RawType = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OtherPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OnTarget = table.Column<bool>(type: "boolean", nullable: true),
                    VideoOffsetSec = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchEvents_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchEvents_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalSchema: "clubs",
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchEvents_Players_OtherPlayerId",
                        column: x => x.OtherPlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchEvents_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "clubs",
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MatchTeamStats",
                schema: "clubs",
                columns: table => new
                {
                    FixtureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsHome = table.Column<bool>(type: "boolean", nullable: false),
                    PossessionPct = table.Column<int>(type: "integer", nullable: false),
                    Shots = table.Column<int>(type: "integer", nullable: false),
                    ShotsOnTarget = table.Column<int>(type: "integer", nullable: false),
                    ShotsSaved = table.Column<int>(type: "integer", nullable: false),
                    ShotsInBox = table.Column<int>(type: "integer", nullable: false),
                    Headers = table.Column<int>(type: "integer", nullable: false),
                    RightFoot = table.Column<int>(type: "integer", nullable: false),
                    LeftFoot = table.Column<int>(type: "integer", nullable: false),
                    Passes = table.Column<int>(type: "integer", nullable: false),
                    PassesCompleted = table.Column<int>(type: "integer", nullable: false),
                    Crosses = table.Column<int>(type: "integer", nullable: false),
                    Interceptions = table.Column<int>(type: "integer", nullable: false),
                    Fouls = table.Column<int>(type: "integer", nullable: false),
                    FoulsDirect = table.Column<int>(type: "integer", nullable: false),
                    FoulsIndirect = table.Column<int>(type: "integer", nullable: false),
                    Offsides = table.Column<int>(type: "integer", nullable: false),
                    Corners = table.Column<int>(type: "integer", nullable: false),
                    CornersRight = table.Column<int>(type: "integer", nullable: false),
                    CornersLeft = table.Column<int>(type: "integer", nullable: false),
                    FreeKicks = table.Column<int>(type: "integer", nullable: false),
                    FreeKicksDirect = table.Column<int>(type: "integer", nullable: false),
                    Dribbles = table.Column<int>(type: "integer", nullable: false),
                    DribbleMeters = table.Column<int>(type: "integer", nullable: false),
                    YellowCards = table.Column<int>(type: "integer", nullable: true),
                    RedCards = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchTeamStats", x => new { x.FixtureId, x.ClubId });
                    table.ForeignKey(
                        name: "FK_MatchTeamStats_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchTeamStats_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalSchema: "clubs",
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_ClubId",
                schema: "clubs",
                table: "MatchEvents",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_FixtureId",
                schema: "clubs",
                table: "MatchEvents",
                column: "FixtureId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_OtherPlayerId",
                schema: "clubs",
                table: "MatchEvents",
                column: "OtherPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_PlayerId",
                schema: "clubs",
                table: "MatchEvents",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeamStats_ClubId",
                schema: "clubs",
                table: "MatchTeamStats",
                column: "ClubId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchEvents",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "MatchTeamStats",
                schema: "clubs");

            migrationBuilder.DropColumn(
                name: "Corners",
                schema: "clubs",
                table: "MatchRatings");

            migrationBuilder.DropColumn(
                name: "Fouls",
                schema: "clubs",
                table: "MatchRatings");

            migrationBuilder.DropColumn(
                name: "Goals",
                schema: "clubs",
                table: "MatchRatings");

            migrationBuilder.DropColumn(
                name: "InMinute",
                schema: "clubs",
                table: "MatchRatings");

            migrationBuilder.DropColumn(
                name: "Offsides",
                schema: "clubs",
                table: "MatchRatings");

            migrationBuilder.DropColumn(
                name: "OutMinute",
                schema: "clubs",
                table: "MatchRatings");

            migrationBuilder.DropColumn(
                name: "RedCards",
                schema: "clubs",
                table: "MatchRatings");

            migrationBuilder.DropColumn(
                name: "Shots",
                schema: "clubs",
                table: "MatchRatings");

            migrationBuilder.DropColumn(
                name: "ShotsOnTarget",
                schema: "clubs",
                table: "MatchRatings");

            migrationBuilder.DropColumn(
                name: "YellowCards",
                schema: "clubs",
                table: "MatchRatings");
        }
    }
}
