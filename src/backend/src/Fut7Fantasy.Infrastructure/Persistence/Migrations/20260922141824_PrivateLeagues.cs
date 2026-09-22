using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivateLeagues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "leagues");

            migrationBuilder.CreateTable(
                name: "PrivateLeagues",
                schema: "leagues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    InviteCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    InviteExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateLeagues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateLeagues_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "competitions",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrivateLeagues_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeagueMemberships",
                schema: "leagues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeagueMemberships_Entries_EntryId",
                        column: x => x.EntryId,
                        principalSchema: "fantasy",
                        principalTable: "Entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeagueMemberships_PrivateLeagues_LeagueId",
                        column: x => x.LeagueId,
                        principalSchema: "leagues",
                        principalTable: "PrivateLeagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeagueMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMemberships_EntryId",
                schema: "leagues",
                table: "LeagueMemberships",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMemberships_LeagueId_EntryId",
                schema: "leagues",
                table: "LeagueMemberships",
                columns: new[] { "LeagueId", "EntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMemberships_UserId",
                schema: "leagues",
                table: "LeagueMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateLeagues_CompetitionId",
                schema: "leagues",
                table: "PrivateLeagues",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateLeagues_InviteCode",
                schema: "leagues",
                table: "PrivateLeagues",
                column: "InviteCode",
                unique: true,
                filter: "[InviteCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateLeagues_OwnerUserId",
                schema: "leagues",
                table: "PrivateLeagues",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeagueMemberships",
                schema: "leagues");

            migrationBuilder.DropTable(
                name: "PrivateLeagues",
                schema: "leagues");
        }
    }
}
