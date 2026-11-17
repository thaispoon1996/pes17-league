using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GoalScorer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ScorerPlayerId",
                schema: "clubs",
                table: "Goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Goals_ScorerPlayerId",
                schema: "clubs",
                table: "Goals",
                column: "ScorerPlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Goals_Players_ScorerPlayerId",
                schema: "clubs",
                table: "Goals",
                column: "ScorerPlayerId",
                principalSchema: "clubs",
                principalTable: "Players",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Goals_Players_ScorerPlayerId",
                schema: "clubs",
                table: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Goals_ScorerPlayerId",
                schema: "clubs",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "ScorerPlayerId",
                schema: "clubs",
                table: "Goals");
        }
    }
}
