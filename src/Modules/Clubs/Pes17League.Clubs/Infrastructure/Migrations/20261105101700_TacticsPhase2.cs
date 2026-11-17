using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TacticsPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CaptainId",
                schema: "clubs",
                table: "Tactics",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeftCornerTakerId",
                schema: "clubs",
                table: "Tactics",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LongFreeKickTakerId",
                schema: "clubs",
                table: "Tactics",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PenaltyTakerId",
                schema: "clubs",
                table: "Tactics",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int[]>(
                name: "Positions",
                schema: "clubs",
                table: "Tactics",
                type: "integer[]",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RightCornerTakerId",
                schema: "clubs",
                table: "Tactics",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShortFreeKickTakerId",
                schema: "clubs",
                table: "Tactics",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int[]>(
                name: "Strategies",
                schema: "clubs",
                table: "Tactics",
                type: "integer[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CaptainId",
                schema: "clubs",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "LeftCornerTakerId",
                schema: "clubs",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "LongFreeKickTakerId",
                schema: "clubs",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "PenaltyTakerId",
                schema: "clubs",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "Positions",
                schema: "clubs",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "RightCornerTakerId",
                schema: "clubs",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "ShortFreeKickTakerId",
                schema: "clubs",
                table: "Tactics");

            migrationBuilder.DropColumn(
                name: "Strategies",
                schema: "clubs",
                table: "Tactics");
        }
    }
}
