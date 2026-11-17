using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MatchdayStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Leg",
                schema: "clubs",
                table: "Matchdays",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                schema: "clubs",
                table: "Matchdays",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Matchday_Leg",
                schema: "clubs",
                table: "Matchdays",
                sql: "\"Leg\" IS NULL OR \"Leg\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Matchday_Leg",
                schema: "clubs",
                table: "Matchdays");

            migrationBuilder.DropColumn(
                name: "Leg",
                schema: "clubs",
                table: "Matchdays");

            migrationBuilder.DropColumn(
                name: "Stage",
                schema: "clubs",
                table: "Matchdays");
        }
    }
}
