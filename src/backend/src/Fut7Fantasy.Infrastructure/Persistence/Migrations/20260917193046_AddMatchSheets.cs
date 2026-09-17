using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchSheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchSheets",
                schema: "competitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HomeScore = table.Column<int>(type: "int", nullable: false),
                    AwayScore = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSheets", x => x.Id);
                    table.CheckConstraint("CK_MatchSheets_AwayScore", "[AwayScore] BETWEEN 0 AND 99");
                    table.CheckConstraint("CK_MatchSheets_HomeScore", "[HomeScore] BETWEEN 0 AND 99");
                    table.ForeignKey(
                        name: "FK_MatchSheets_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "competitions",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchSheets_Matches_MatchId",
                        column: x => x.MatchId,
                        principalSchema: "competitions",
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AthleteAppearances",
                schema: "competitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchSheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AthleteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    DidPlay = table.Column<bool>(type: "bit", nullable: false),
                    PlayedAsGoalkeeper = table.Column<bool>(type: "bit", nullable: false),
                    GoalsConceded = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AthleteAppearances", x => x.Id);
                    table.CheckConstraint("CK_AthleteAppearances_GoalsConceded", "[GoalsConceded] BETWEEN 0 AND 99");
                    table.ForeignKey(
                        name: "FK_AthleteAppearances_Athletes_AthleteId",
                        column: x => x.AthleteId,
                        principalSchema: "sports_catalog",
                        principalTable: "Athletes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AthleteAppearances_MatchSheets_MatchSheetId",
                        column: x => x.MatchSheetId,
                        principalSchema: "competitions",
                        principalTable: "MatchSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AthleteAppearances_RealTeams_RealTeamId",
                        column: x => x.RealTeamId,
                        principalSchema: "sports_catalog",
                        principalTable: "RealTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StatEvents",
                schema: "competitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchSheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AthleteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    RedCardReason = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatEvents", x => x.Id);
                    table.CheckConstraint("CK_StatEvents_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_StatEvents_Athletes_AthleteId",
                        column: x => x.AthleteId,
                        principalSchema: "sports_catalog",
                        principalTable: "Athletes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatEvents_MatchSheets_MatchSheetId",
                        column: x => x.MatchSheetId,
                        principalSchema: "competitions",
                        principalTable: "MatchSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AthleteAppearances_AthleteId",
                schema: "competitions",
                table: "AthleteAppearances",
                column: "AthleteId");

            migrationBuilder.CreateIndex(
                name: "IX_AthleteAppearances_CompetitionId_MatchSheetId",
                schema: "competitions",
                table: "AthleteAppearances",
                columns: new[] { "CompetitionId", "MatchSheetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AthleteAppearances_MatchSheetId_AthleteId",
                schema: "competitions",
                table: "AthleteAppearances",
                columns: new[] { "MatchSheetId", "AthleteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AthleteAppearances_RealTeamId",
                schema: "competitions",
                table: "AthleteAppearances",
                column: "RealTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSheets_CompetitionId_MatchId",
                schema: "competitions",
                table: "MatchSheets",
                columns: new[] { "CompetitionId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchSheets_MatchId",
                schema: "competitions",
                table: "MatchSheets",
                column: "MatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatEvents_AthleteId",
                schema: "competitions",
                table: "StatEvents",
                column: "AthleteId");

            migrationBuilder.CreateIndex(
                name: "IX_StatEvents_CompetitionId_MatchSheetId",
                schema: "competitions",
                table: "StatEvents",
                columns: new[] { "CompetitionId", "MatchSheetId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatEvents_MatchSheetId_AthleteId_Type",
                schema: "competitions",
                table: "StatEvents",
                columns: new[] { "MatchSheetId", "AthleteId", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AthleteAppearances",
                schema: "competitions");

            migrationBuilder.DropTable(
                name: "StatEvents",
                schema: "competitions");

            migrationBuilder.DropTable(
                name: "MatchSheets",
                schema: "competitions");
        }
    }
}
