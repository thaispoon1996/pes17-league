using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlayerAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "FixtureId",
                schema: "clubs",
                table: "AttributeChanges",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "At",
                schema: "clubs",
                table: "AttributeChanges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "clubs",
                table: "AttributeChanges",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Match");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "At",
                schema: "clubs",
                table: "AttributeChanges");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "clubs",
                table: "AttributeChanges");

            migrationBuilder.AlterColumn<Guid>(
                name: "FixtureId",
                schema: "clubs",
                table: "AttributeChanges",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
