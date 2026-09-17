using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SportsCatalogAthletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Athletes",
                schema: "sports_catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SportingName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FirstMarketAvailableAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Athletes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Athletes_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "competitions",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RosterRegistrations",
                schema: "sports_catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AthleteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceTier = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    InitialPriceOverride = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterRegistrations", x => x.Id);
                    table.CheckConstraint("CK_RosterRegistrations_InitialPriceOverride", "[InitialPriceOverride] IS NULL OR [InitialPriceOverride] BETWEEN 1 AND 30");
                    table.ForeignKey(
                        name: "FK_RosterRegistrations_Athletes_AthleteId",
                        column: x => x.AthleteId,
                        principalSchema: "sports_catalog",
                        principalTable: "Athletes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RosterRegistrations_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "competitions",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RosterRegistrations_RealTeams_RealTeamId",
                        column: x => x.RealTeamId,
                        principalSchema: "sports_catalog",
                        principalTable: "RealTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Athletes_CompetitionId_SportingName",
                schema: "sports_catalog",
                table: "Athletes",
                columns: new[] { "CompetitionId", "SportingName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterRegistrations_AthleteId",
                schema: "sports_catalog",
                table: "RosterRegistrations",
                column: "AthleteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterRegistrations_CompetitionId_RealTeamId_Status",
                schema: "sports_catalog",
                table: "RosterRegistrations",
                columns: new[] { "CompetitionId", "RealTeamId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RosterRegistrations_RealTeamId",
                schema: "sports_catalog",
                table: "RosterRegistrations",
                column: "RealTeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RosterRegistrations",
                schema: "sports_catalog");

            migrationBuilder.DropTable(
                name: "Athletes",
                schema: "sports_catalog");
        }
    }
}
