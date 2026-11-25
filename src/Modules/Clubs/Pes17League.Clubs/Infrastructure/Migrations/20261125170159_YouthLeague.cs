using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class YouthLeague : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YouthAppearances",
                schema: "clubs",
                columns: table => new
                {
                    FixtureId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    Goals = table.Column<int>(type: "integer", nullable: false),
                    Started = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouthAppearances", x => new { x.FixtureId, x.PlayerId });
                });

            migrationBuilder.CreateTable(
                name: "YouthFixtures",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EditionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    HomeClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeGoals = table.Column<int>(type: "integer", nullable: true),
                    AwayGoals = table.Column<int>(type: "integer", nullable: true),
                    MatchdayId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlayedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouthFixtures", x => x.Id);
                    table.CheckConstraint("CK_YouthFixture_Goals", "(\"HomeGoals\" IS NULL) = (\"AwayGoals\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "YouthPlayers",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Position = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: false),
                    Overall = table.Column<int>(type: "integer", nullable: false),
                    Potential = table.Column<int>(type: "integer", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    JoinedSeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouthPlayers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YouthAppearances_PlayerId",
                schema: "clubs",
                table: "YouthAppearances",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_YouthFixtures_EditionId_Round",
                schema: "clubs",
                table: "YouthFixtures",
                columns: new[] { "EditionId", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_YouthPlayers_ClubId",
                schema: "clubs",
                table: "YouthPlayers",
                column: "ClubId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YouthAppearances",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "YouthFixtures",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "YouthPlayers",
                schema: "clubs");
        }
    }
}
