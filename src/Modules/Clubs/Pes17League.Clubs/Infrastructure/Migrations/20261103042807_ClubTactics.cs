using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClubTactics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tactics",
                schema: "clubs",
                columns: table => new
                {
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Formation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    BackLine = table.Column<int>(type: "integer", nullable: true),
                    Pressure = table.Column<int>(type: "integer", nullable: true),
                    OffsideTrap = table.Column<int>(type: "integer", nullable: true),
                    CounterAttack = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tactics", x => x.ClubId);
                    table.ForeignKey(
                        name: "FK_Tactics_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tactics",
                schema: "clubs");
        }
    }
}
