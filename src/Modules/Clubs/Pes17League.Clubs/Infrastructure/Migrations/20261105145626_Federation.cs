using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Federation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompetitionEditionId",
                schema: "clubs",
                table: "Fixtures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Leg",
                schema: "clubs",
                table: "Fixtures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                schema: "clubs",
                table: "Fixtures",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "League");

            migrationBuilder.AddColumn<Guid>(
                name: "TieId",
                schema: "clubs",
                table: "Fixtures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                schema: "clubs",
                table: "Clubs",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoPath",
                schema: "clubs",
                table: "Clubs",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminAudit",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: true),
                    After = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Competitions",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Country = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Tier = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LogoPath = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Rules = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PromotesToId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelegatesToId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitions", x => x.Id);
                    table.CheckConstraint("CK_Competition_Tier", "\"Tier\" IS NULL OR \"Tier\" >= 1");
                    table.ForeignKey(
                        name: "FK_Competitions_Competitions_PromotesToId",
                        column: x => x.PromotesToId,
                        principalSchema: "clubs",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Competitions_Competitions_RelegatesToId",
                        column: x => x.RelegatesToId,
                        principalSchema: "clubs",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Editions",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RulesSnapshot = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Editions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Editions_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "clubs",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Editions_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "clubs",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EditionClubs",
                schema: "clubs",
                columns: table => new
                {
                    EditionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seed = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditionClubs", x => new { x.EditionId, x.ClubId });
                    table.ForeignKey(
                        name: "FK_EditionClubs_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EditionClubs_Editions_EditionId",
                        column: x => x.EditionId,
                        principalSchema: "clubs",
                        principalTable: "Editions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_CompetitionEditionId",
                schema: "clubs",
                table: "Fixtures",
                column: "CompetitionEditionId");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_TieId",
                schema: "clubs",
                table: "Fixtures",
                column: "TieId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Fixture_Leg",
                schema: "clubs",
                table: "Fixtures",
                sql: "\"Leg\" IS NULL OR \"Leg\" IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAudit_EntityType_EntityId",
                schema: "clubs",
                table: "AdminAudit",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_Name",
                schema: "clubs",
                table: "Competitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_PromotesToId",
                schema: "clubs",
                table: "Competitions",
                column: "PromotesToId");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_RelegatesToId",
                schema: "clubs",
                table: "Competitions",
                column: "RelegatesToId");

            migrationBuilder.CreateIndex(
                name: "IX_EditionClubs_ClubId",
                schema: "clubs",
                table: "EditionClubs",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Editions_CompetitionId_SeasonId",
                schema: "clubs",
                table: "Editions",
                columns: new[] { "CompetitionId", "SeasonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Editions_SeasonId",
                schema: "clubs",
                table: "Editions",
                column: "SeasonId");

            migrationBuilder.AddForeignKey(
                name: "FK_Fixtures_Editions_CompetitionEditionId",
                schema: "clubs",
                table: "Fixtures",
                column: "CompetitionEditionId",
                principalSchema: "clubs",
                principalTable: "Editions",
                principalColumn: "Id");

            // Dữ liệu cũ (PO chốt Q3 05/11): mỗi mùa hiện có → 1 edition của "Giải thử nghiệm"; CLB lấy từ SeasonClubs, trận gán qua vòng.
            // Chỉ thêm, không xoá; mùa cũ giữ trạng thái (Finished/lưu trữ làm bằng thao tác admin).
            migrationBuilder.Sql("""
                INSERT INTO clubs."Competitions" ("Id", "Name", "ShortName", "Country", "Tier", "Type", "Rules", "Status", "CreatedAt")
                SELECT gen_random_uuid(), 'Giải thử nghiệm', 'Thử nghiệm', 'ENG', 1, 'DomesticLeague',
                       '{"PointsWin":3,"PointsDraw":1,"PointsLoss":0,"Legs":2,"PromotionPlaces":0,"RelegationPlaces":0,"ContinentalPlaces":0,"SecondaryContinentalPlaces":0}',
                       'Archived', now()
                WHERE EXISTS (SELECT 1 FROM clubs."Seasons");
                INSERT INTO clubs."Editions" ("Id", "CompetitionId", "SeasonId", "Status", "RulesSnapshot")
                SELECT gen_random_uuid(), c."Id", s."Id", CASE s."Status" WHEN 'Planned' THEN 'Draft' ELSE s."Status" END, c."Rules"
                FROM clubs."Seasons" s CROSS JOIN clubs."Competitions" c WHERE c."Name" = 'Giải thử nghiệm';
                INSERT INTO clubs."EditionClubs" ("EditionId", "ClubId")
                SELECT e."Id", sc."ClubId" FROM clubs."SeasonClubs" sc JOIN clubs."Editions" e ON e."SeasonId" = sc."SeasonId";
                UPDATE clubs."Fixtures" f SET "CompetitionEditionId" = e."Id"
                FROM clubs."Matchdays" m JOIN clubs."Editions" e ON e."SeasonId" = m."SeasonId"
                WHERE f."MatchdayId" = m."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Fixtures_Editions_CompetitionEditionId",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropTable(
                name: "AdminAudit",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "EditionClubs",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Editions",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "Competitions",
                schema: "clubs");

            migrationBuilder.DropIndex(
                name: "IX_Fixtures_CompetitionEditionId",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropIndex(
                name: "IX_Fixtures_TieId",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Fixture_Leg",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "CompetitionEditionId",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "Leg",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "Stage",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "TieId",
                schema: "clubs",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "Country",
                schema: "clubs",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "LogoPath",
                schema: "clubs",
                table: "Clubs");
        }
    }
}
