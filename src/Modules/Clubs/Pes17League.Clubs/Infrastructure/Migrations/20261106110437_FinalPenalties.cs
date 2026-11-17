using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinalPenalties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AwayPenalties",
                schema: "clubs",
                table: "Fixtures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HomePenalties",
                schema: "clubs",
                table: "Fixtures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Fixture_Penalties",
                schema: "clubs",
                table: "Fixtures",
                sql: "(\"HomePenalties\" IS NULL AND \"AwayPenalties\" IS NULL) OR (\"HomePenalties\" >= 0 AND \"AwayPenalties\" >= 0 AND \"HomePenalties\" <> \"AwayPenalties\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Fixture_Penalties",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "AwayPenalties",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "HomePenalties",
                schema: "clubs",
                table: "Fixtures");
        }
    }
}
