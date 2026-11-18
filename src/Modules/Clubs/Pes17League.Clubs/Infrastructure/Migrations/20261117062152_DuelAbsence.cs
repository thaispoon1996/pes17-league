using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DuelAbsence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MissWarnedAt",
                schema: "clubs",
                table: "TeamAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissedInRow",
                schema: "clubs",
                table: "TeamAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AwayAbsent",
                schema: "clubs",
                table: "Fixtures",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AwayReadyAt",
                schema: "clubs",
                table: "Fixtures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HomeAbsent",
                schema: "clubs",
                table: "Fixtures",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HomeReadyAt",
                schema: "clubs",
                table: "Fixtures",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MissWarnedAt",
                schema: "clubs",
                table: "TeamAssignments");

            migrationBuilder.DropColumn(
                name: "MissedInRow",
                schema: "clubs",
                table: "TeamAssignments");

            migrationBuilder.DropColumn(
                name: "AwayAbsent",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "AwayReadyAt",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "HomeAbsent",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "HomeReadyAt",
                schema: "clubs",
                table: "Fixtures");
        }
    }
}
