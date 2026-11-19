using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CareerSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "FriendlyWinPrize",
                schema: "clubs",
                table: "SeasonEconomies",
                type: "bigint",
                nullable: false,
                defaultValue: 12500L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 37500L);

            migrationBuilder.AlterColumn<long>(
                name: "FriendlyDrawPrize",
                schema: "clubs",
                table: "SeasonEconomies",
                type: "bigint",
                nullable: false,
                defaultValue: 3750L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 12500L);

            migrationBuilder.CreateTable(
                name: "CareerSeen",
                schema: "clubs",
                columns: table => new
                {
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerSeen", x => new { x.ParticipantId, x.Key });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareerSeen",
                schema: "clubs");

            migrationBuilder.AlterColumn<long>(
                name: "FriendlyWinPrize",
                schema: "clubs",
                table: "SeasonEconomies",
                type: "bigint",
                nullable: false,
                defaultValue: 37500L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 12500L);

            migrationBuilder.AlterColumn<long>(
                name: "FriendlyDrawPrize",
                schema: "clubs",
                table: "SeasonEconomies",
                type: "bigint",
                nullable: false,
                defaultValue: 12500L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 3750L);
        }
    }
}
