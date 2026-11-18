using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pes17League.Clubs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Mail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MailBlocks",
                schema: "clubs",
                columns: table => new
                {
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailBlocks", x => new { x.ManagerId, x.BlockedManagerId });
                    table.ForeignKey(
                        name: "FK_MailBlocks_Managers_BlockedManagerId",
                        column: x => x.BlockedManagerId,
                        principalSchema: "clubs",
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MailBlocks_Managers_ManagerId",
                        column: x => x.ManagerId,
                        principalSchema: "clubs",
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MailMessages",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Subject = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Template = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    FixtureId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SenderArchived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailMessages_Clubs_FromClubId",
                        column: x => x.FromClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MailMessages_Managers_FromManagerId",
                        column: x => x.FromManagerId,
                        principalSchema: "clubs",
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MailRecipients",
                schema: "clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailRecipients_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalSchema: "clubs",
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MailRecipients_MailMessages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "clubs",
                        principalTable: "MailMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MailRecipients_Managers_ManagerId",
                        column: x => x.ManagerId,
                        principalSchema: "clubs",
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailBlocks_BlockedManagerId",
                schema: "clubs",
                table: "MailBlocks",
                column: "BlockedManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_MailMessages_FromClubId",
                schema: "clubs",
                table: "MailMessages",
                column: "FromClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MailMessages_FromManagerId_At",
                schema: "clubs",
                table: "MailMessages",
                columns: new[] { "FromManagerId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_MailMessages_ThreadId",
                schema: "clubs",
                table: "MailMessages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_MailRecipients_ClubId",
                schema: "clubs",
                table: "MailRecipients",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MailRecipients_ManagerId_ReadAt",
                schema: "clubs",
                table: "MailRecipients",
                columns: new[] { "ManagerId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MailRecipients_MessageId",
                schema: "clubs",
                table: "MailRecipients",
                column: "MessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailBlocks",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "MailRecipients",
                schema: "clubs");

            migrationBuilder.DropTable(
                name: "MailMessages",
                schema: "clubs");
        }
    }
}
