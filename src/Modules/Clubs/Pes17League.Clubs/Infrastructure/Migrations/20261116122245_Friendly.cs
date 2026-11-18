using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Friendly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FriendlyDrawPrize",
                schema: "clubs",
                table: "SeasonEconomies",
                type: "bigint",
                nullable: false,
                defaultValue: 12500L);

            migrationBuilder.AddColumn<long>(
                name: "FriendlyWinPrize",
                schema: "clubs",
                table: "SeasonEconomies",
                type: "bigint",
                nullable: false,
                defaultValue: 37500L);

            migrationBuilder.AddColumn<int>(
                name: "Elo",
                schema: "clubs",
                table: "Managers",
                type: "integer",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.CreateTable(
                name: "FriendlyMatches",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    HomeManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwayClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: true),
                    HomePesTeam = table.Column<int>(type: "integer", nullable: true),
                    AwayPesTeam = table.Column<int>(type: "integer", nullable: true),
                    HomeTeamName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    AwayTeamName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    HomeReady = table.Column<bool>(type: "boolean", nullable: false),
                    AwayReady = table.Column<bool>(type: "boolean", nullable: false),
                    PickDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HomeGoals = table.Column<int>(type: "integer", nullable: true),
                    AwayGoals = table.Column<int>(type: "integer", nullable: true),
                    AbandonedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    EloHomeDelta = table.Column<int>(type: "integer", nullable: true),
                    EloAwayDelta = table.Column<int>(type: "integer", nullable: true),
                    Counts = table.Column<bool>(type: "boolean", nullable: false),
                    RematchOf = table.Column<Guid>(type: "uuid", nullable: true),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendlyMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FriendlyMatches_Clubs_AwayClubId",
                        column: x => x.AwayClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FriendlyMatches_Clubs_HomeClubId",
                        column: x => x.HomeClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FriendlyMatches_Managers_AwayManagerId",
                        column: x => x.AwayManagerId,
                        principalSchema: "clubs",
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FriendlyMatches_Managers_HomeManagerId",
                        column: x => x.HomeManagerId,
                        principalSchema: "clubs",
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FriendlyMatches_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "clubs",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FriendlyChat",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendlyChat", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FriendlyChat_FriendlyMatches_MatchId",
                        column: x => x.MatchId,
                        principalSchema: "clubs",
                        principalTable: "FriendlyMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FriendlyChat_Managers_ManagerId",
                        column: x => x.ManagerId,
                        principalSchema: "clubs",
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FriendlyGoals",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Home = table.Column<bool>(type: "boolean", nullable: false),
                    Half = table.Column<int>(type: "integer", nullable: false),
                    Minute = table.Column<int>(type: "integer", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: true),
                    ScorerName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendlyGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FriendlyGoals_FriendlyMatches_MatchId",
                        column: x => x.MatchId,
                        principalSchema: "clubs",
                        principalTable: "FriendlyMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchmakingTickets",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Elo = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchmakingTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchmakingTickets_FriendlyMatches_MatchId",
                        column: x => x.MatchId,
                        principalSchema: "clubs",
                        principalTable: "FriendlyMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MatchmakingTickets_Managers_ManagerId",
                        column: x => x.ManagerId,
                        principalSchema: "clubs",
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FriendlyChat_ManagerId",
                schema: "clubs",
                table: "FriendlyChat",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_FriendlyChat_MatchId_At",
                schema: "clubs",
                table: "FriendlyChat",
                columns: new[] { "MatchId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_FriendlyGoals_MatchId",
                schema: "clubs",
                table: "FriendlyGoals",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FriendlyMatches_AwayClubId",
                schema: "clubs",
                table: "FriendlyMatches",
                column: "AwayClubId");

            migrationBuilder.CreateIndex(
                name: "IX_FriendlyMatches_AwayManagerId_CreatedAt",
                schema: "clubs",
                table: "FriendlyMatches",
                columns: new[] { "AwayManagerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FriendlyMatches_HomeClubId",
                schema: "clubs",
                table: "FriendlyMatches",
                column: "HomeClubId");

            migrationBuilder.CreateIndex(
                name: "IX_FriendlyMatches_HomeManagerId_CreatedAt",
                schema: "clubs",
                table: "FriendlyMatches",
                columns: new[] { "HomeManagerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FriendlyMatches_SeasonId_Mode",
                schema: "clubs",
                table: "FriendlyMatches",
                columns: new[] { "SeasonId", "Mode" });

            migrationBuilder.CreateIndex(
                name: "IX_FriendlyMatches_Status",
                schema: "clubs",
                table: "FriendlyMatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingTickets_ManagerId",
                schema: "clubs",
                table: "MatchmakingTickets",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingTickets_MatchId",
                schema: "clubs",
                table: "MatchmakingTickets",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingTickets_Mode_ExpiresAt",
                schema: "clubs",
                table: "MatchmakingTickets",
                columns: new[] { "Mode", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FriendlyChat",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "FriendlyGoals",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "MatchmakingTickets",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "FriendlyMatches",
                schema: "clubs");

            migrationBuilder.DropColumn(
                name: "FriendlyDrawPrize",
                schema: "clubs",
                table: "SeasonEconomies");

            migrationBuilder.DropColumn(
                name: "FriendlyWinPrize",
                schema: "clubs",
                table: "SeasonEconomies");

            migrationBuilder.DropColumn(
                name: "Elo",
                schema: "clubs",
                table: "Managers");
        }
    }
}
