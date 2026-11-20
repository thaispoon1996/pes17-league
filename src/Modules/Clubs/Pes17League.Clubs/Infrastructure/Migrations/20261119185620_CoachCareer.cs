using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CoachCareer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoachApplications",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Invited = table.Column<bool>(type: "boolean", nullable: false),
                    Patience = table.Column<int>(type: "integer", nullable: false),
                    BaseWage = table.Column<long>(type: "bigint", nullable: false),
                    Wage = table.Column<long>(type: "bigint", nullable: false),
                    Bonus = table.Column<long>(type: "bigint", nullable: false),
                    Seasons = table.Column<int>(type: "integer", nullable: false),
                    SeverancePct = table.Column<int>(type: "integer", nullable: false),
                    Log = table.Column<string>(type: "text", nullable: false),
                    Tick = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoachAssets",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    MaturesTick = table.Column<int>(type: "integer", nullable: true),
                    ReturnPermille = table.Column<int>(type: "integer", nullable: false),
                    Tick = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoachContracts",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seasons = table.Column<int>(type: "integer", nullable: false),
                    EndSeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    Wage = table.Column<long>(type: "bigint", nullable: false),
                    Bonus = table.Column<long>(type: "bigint", nullable: false),
                    SeverancePct = table.Column<int>(type: "integer", nullable: false),
                    ExpectedPosition = table.Column<int>(type: "integer", nullable: false),
                    Objective = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RoundsPaid = table.Column<int>(type: "integer", nullable: false),
                    TotalRounds = table.Column<int>(type: "integer", nullable: false),
                    SignedTick = table.Column<int>(type: "integer", nullable: false),
                    EndedTick = table.Column<int>(type: "integer", nullable: true),
                    Launched = table.Column<bool>(type: "boolean", nullable: false),
                    RenewAgreed = table.Column<bool>(type: "boolean", nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachContracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoachDrills",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Applied = table.Column<bool>(type: "boolean", nullable: false),
                    Tick = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachDrills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Coaches",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Nickname = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Age = table.Column<int>(type: "integer", nullable: false),
                    Nationality = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Formation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Style = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Trait = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Draws = table.Column<int>(type: "integer", nullable: false),
                    Seed = table.Column<int>(type: "integer", nullable: false),
                    Skills = table.Column<int[]>(type: "integer[]", nullable: false),
                    BaseSkills = table.Column<int[]>(type: "integer[]", nullable: false),
                    FreePoints = table.Column<int>(type: "integer", nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false),
                    License = table.Column<int>(type: "integer", nullable: false),
                    CourseLicense = table.Column<int>(type: "integer", nullable: false),
                    CourseRoundsLeft = table.Column<int>(type: "integer", nullable: false),
                    Reputation = table.Column<int>(type: "integer", nullable: false),
                    Stress = table.Column<int>(type: "integer", nullable: false),
                    Lifestyle = table.Column<int>(type: "integer", nullable: false),
                    Hair = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HairColor = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    Outfit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Skin = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    Face = table.Column<byte[]>(type: "bytea", nullable: true),
                    FaceMesh = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tick = table.Column<int>(type: "integer", nullable: false),
                    WarnedTick = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coaches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoachLedger",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tick = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachLedger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoachLetters",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    From = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Sender = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Options = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DefaultOption = table.Column<int>(type: "integer", nullable: false),
                    Chosen = table.Column<int>(type: "integer", nullable: true),
                    DeadlineTick = table.Column<int>(type: "integer", nullable: true),
                    AnsweredTick = table.Column<int>(type: "integer", nullable: true),
                    Outgoing = table.Column<bool>(type: "boolean", nullable: false),
                    ReplyToId = table.Column<Guid>(type: "uuid", nullable: true),
                    Data = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Link = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Read = table.Column<bool>(type: "boolean", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    Tick = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachLetters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoachPhotos",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoachId = table.Column<Guid>(type: "uuid", nullable: false),
                    Caption = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Image = table.Column<byte[]>(type: "bytea", nullable: false),
                    Tick = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachPhotos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachApplications_CoachId_SeasonId_ClubId",
                schema: "clubs",
                table: "CoachApplications",
                columns: new[] { "CoachId", "SeasonId", "ClubId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachAssets_CoachId",
                schema: "clubs",
                table: "CoachAssets",
                column: "CoachId");

            migrationBuilder.CreateIndex(
                name: "IX_CoachContracts_CoachId_Status",
                schema: "clubs",
                table: "CoachContracts",
                columns: new[] { "CoachId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachDrills_ClubId_Applied",
                schema: "clubs",
                table: "CoachDrills",
                columns: new[] { "ClubId", "Applied" });

            migrationBuilder.CreateIndex(
                name: "IX_Coaches_ManagerId",
                schema: "clubs",
                table: "Coaches",
                column: "ManagerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoachLedger_CoachId",
                schema: "clubs",
                table: "CoachLedger",
                column: "CoachId");

            migrationBuilder.CreateIndex(
                name: "IX_CoachLetters_CoachId_Tick",
                schema: "clubs",
                table: "CoachLetters",
                columns: new[] { "CoachId", "Tick" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachPhotos_CoachId",
                schema: "clubs",
                table: "CoachPhotos",
                column: "CoachId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachApplications",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "CoachAssets",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "CoachContracts",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "CoachDrills",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Coaches",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "CoachLedger",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "CoachLetters",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "CoachPhotos",
                schema: "clubs");
        }
    }
}
