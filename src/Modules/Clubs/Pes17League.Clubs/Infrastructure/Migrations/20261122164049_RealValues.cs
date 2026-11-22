using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RealValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RealValue",
                schema: "clubs",
                table: "Players",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealValueAge",
                schema: "clubs",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealValueOverall",
                schema: "clubs",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealValueRef",
                schema: "clubs",
                table: "Players",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RealValue",
                schema: "clubs",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "RealValueAge",
                schema: "clubs",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "RealValueOverall",
                schema: "clubs",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "RealValueRef",
                schema: "clubs",
                table: "Players");
        }
    }
}
