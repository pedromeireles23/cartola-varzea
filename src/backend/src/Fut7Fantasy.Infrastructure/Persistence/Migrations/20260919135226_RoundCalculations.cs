using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoundCalculations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scoring");

            migrationBuilder.CreateTable(
                name: "RoundCalculations",
                schema: "scoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Modality = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ScoringRuleSetVersion = table.Column<int>(type: "int", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CalculatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundCalculations", x => x.Id);
                    table.CheckConstraint("CK_RoundCalculations_Revision", "[Revision] >= 1");
                    table.ForeignKey(
                        name: "FK_RoundCalculations_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "competitions",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoundCalculations_Rounds_RoundId",
                        column: x => x.RoundId,
                        principalSchema: "competitions",
                        principalTable: "Rounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoundCalculations_Users_CalculatedBy",
                        column: x => x.CalculatedBy,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetPriceChanges",
                schema: "scoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousPrice = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Average = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    Difference = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    Variation = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    NewPrice = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetPriceChanges", x => x.Id);
                    table.CheckConstraint("CK_AssetPriceChanges_NewPrice", "[NewPrice] >= 1 AND [NewPrice] <= 30");
                    table.ForeignKey(
                        name: "FK_AssetPriceChanges_RoundCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalSchema: "scoring",
                        principalTable: "RoundCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AthleteRoundResults",
                schema: "scoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AthleteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Played = table.Column<bool>(type: "bit", nullable: false),
                    Points = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AthleteRoundResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AthleteRoundResults_RoundCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalSchema: "scoring",
                        principalTable: "RoundCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AthleteScoreLines",
                schema: "scoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AthleteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Item = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AthleteScoreLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AthleteScoreLines_RoundCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalSchema: "scoring",
                        principalTable: "RoundCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoachRoundResults",
                schema: "scoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Played = table.Column<bool>(type: "bit", nullable: false),
                    Points = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachRoundResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoachRoundResults_RoundCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalSchema: "scoring",
                        principalTable: "RoundCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EntryRoundResults",
                schema: "scoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    CaptainBonus = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryRoundResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntryRoundResults_Entries_EntryId",
                        column: x => x.EntryId,
                        principalSchema: "fantasy",
                        principalTable: "Entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntryRoundResults_RoundCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalSchema: "scoring",
                        principalTable: "RoundCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EntrySlotResults",
                schema: "scoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Played = table.Column<bool>(type: "bit", nullable: false),
                    Points = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                    Counts = table.Column<bool>(type: "bit", nullable: false),
                    Replaces = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReplacedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntrySlotResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntrySlotResults_RoundCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalSchema: "scoring",
                        principalTable: "RoundCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PositionAverages",
                schema: "scoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Average = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionAverages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionAverages_RoundCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalSchema: "scoring",
                        principalTable: "RoundCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetPriceChanges_CalculationId_Kind_AssetId",
                schema: "scoring",
                table: "AssetPriceChanges",
                columns: new[] { "CalculationId", "Kind", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetPriceChanges_Kind_AssetId",
                schema: "scoring",
                table: "AssetPriceChanges",
                columns: new[] { "Kind", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AthleteRoundResults_CalculationId_AthleteId",
                schema: "scoring",
                table: "AthleteRoundResults",
                columns: new[] { "CalculationId", "AthleteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AthleteScoreLines_CalculationId_AthleteId_MatchId_Item",
                schema: "scoring",
                table: "AthleteScoreLines",
                columns: new[] { "CalculationId", "AthleteId", "MatchId", "Item" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoachRoundResults_CalculationId_CoachId",
                schema: "scoring",
                table: "CoachRoundResults",
                columns: new[] { "CalculationId", "CoachId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntryRoundResults_CalculationId_EntryId",
                schema: "scoring",
                table: "EntryRoundResults",
                columns: new[] { "CalculationId", "EntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntryRoundResults_EntryId",
                schema: "scoring",
                table: "EntryRoundResults",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_EntrySlotResults_CalculationId_EntryId_Kind_AssetId",
                schema: "scoring",
                table: "EntrySlotResults",
                columns: new[] { "CalculationId", "EntryId", "Kind", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PositionAverages_CalculationId_Kind_Position",
                schema: "scoring",
                table: "PositionAverages",
                columns: new[] { "CalculationId", "Kind", "Position" },
                unique: true,
                filter: "[Position] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoundCalculations_CalculatedBy",
                schema: "scoring",
                table: "RoundCalculations",
                column: "CalculatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RoundCalculations_CompetitionId",
                schema: "scoring",
                table: "RoundCalculations",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundCalculations_RoundId_Revision",
                schema: "scoring",
                table: "RoundCalculations",
                columns: new[] { "RoundId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetPriceChanges",
                schema: "scoring");

            migrationBuilder.DropTable(
                name: "AthleteRoundResults",
                schema: "scoring");

            migrationBuilder.DropTable(
                name: "AthleteScoreLines",
                schema: "scoring");

            migrationBuilder.DropTable(
                name: "CoachRoundResults",
                schema: "scoring");

            migrationBuilder.DropTable(
                name: "EntryRoundResults",
                schema: "scoring");

            migrationBuilder.DropTable(
                name: "EntrySlotResults",
                schema: "scoring");

            migrationBuilder.DropTable(
                name: "PositionAverages",
                schema: "scoring");

            migrationBuilder.DropTable(
                name: "RoundCalculations",
                schema: "scoring");
        }
    }
}
