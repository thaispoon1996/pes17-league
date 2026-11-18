using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeagueApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeagueApplications",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferredClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeagueApplications_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LeagueApplications_Clubs_PreferredClubId",
                        column: x => x.PreferredClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LeagueApplications_Managers_ManagerId",
                        column: x => x.ManagerId,
                        principalSchema: "clubs",
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueApplications_ClubId",
                schema: "clubs",
                table: "LeagueApplications",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueApplications_ManagerId_Status",
                schema: "clubs",
                table: "LeagueApplications",
                columns: new[] { "ManagerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueApplications_PreferredClubId",
                schema: "clubs",
                table: "LeagueApplications",
                column: "PreferredClubId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeagueApplications",
                schema: "clubs");
        }
    }
}
