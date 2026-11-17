using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GoalVotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoalPolls",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchdayId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionEditionId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosesAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WinnerGoalId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalPolls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalPolls_Editions_CompetitionEditionId",
                        column: x => x.CompetitionEditionId,
                        principalSchema: "clubs",
                        principalTable: "Editions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GoalPolls_Goals_WinnerGoalId",
                        column: x => x.WinnerGoalId,
                        principalSchema: "clubs",
                        principalTable: "Goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GoalPolls_Matchdays_MatchdayId",
                        column: x => x.MatchdayId,
                        principalSchema: "clubs",
                        principalTable: "Matchdays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GoalVotes",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PollId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    VoterKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CastAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalVotes_GoalPolls_PollId",
                        column: x => x.PollId,
                        principalSchema: "clubs",
                        principalTable: "GoalPolls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoalVotes_Goals_GoalId",
                        column: x => x.GoalId,
                        principalSchema: "clubs",
                        principalTable: "Goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoalPolls_CompetitionEditionId",
                schema: "clubs",
                table: "GoalPolls",
                column: "CompetitionEditionId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalPolls_MatchdayId_CompetitionEditionId",
                schema: "clubs",
                table: "GoalPolls",
                columns: new[] { "MatchdayId", "CompetitionEditionId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_GoalPolls_SeasonId",
                schema: "clubs",
                table: "GoalPolls",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalPolls_WinnerGoalId",
                schema: "clubs",
                table: "GoalPolls",
                column: "WinnerGoalId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalVotes_GoalId",
                schema: "clubs",
                table: "GoalVotes",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalVotes_PollId_IpHash",
                schema: "clubs",
                table: "GoalVotes",
                columns: new[] { "PollId", "IpHash" });

            migrationBuilder.CreateIndex(
                name: "IX_GoalVotes_PollId_VoterKey",
                schema: "clubs",
                table: "GoalVotes",
                columns: new[] { "PollId", "VoterKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoalVotes",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "GoalPolls",
                schema: "clubs");
        }
    }
}
