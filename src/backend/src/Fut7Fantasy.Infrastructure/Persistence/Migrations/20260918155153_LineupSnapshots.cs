using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LineupSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LineupSnapshots",
                schema: "fantasy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaptainAthleteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModalityProfileVersion = table.Column<int>(type: "int", nullable: false),
                    MaxStartersPerTeam = table.Column<int>(type: "int", nullable: false),
                    MaxAthletesPerTeam = table.Column<int>(type: "int", nullable: false),
                    MarketClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineupSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineupSnapshots_Entries_EntryId",
                        column: x => x.EntryId,
                        principalSchema: "fantasy",
                        principalTable: "Entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LineupSnapshots_Rounds_RoundId",
                        column: x => x.RoundId,
                        principalSchema: "competitions",
                        principalTable: "Rounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LineupSnapshotSlots",
                schema: "fantasy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    RealTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealTeamName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineupSnapshotSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineupSnapshotSlots_LineupSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalSchema: "fantasy",
                        principalTable: "LineupSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LineupSnapshots_EntryId_RoundId",
                schema: "fantasy",
                table: "LineupSnapshots",
                columns: new[] { "EntryId", "RoundId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineupSnapshots_RoundId",
                schema: "fantasy",
                table: "LineupSnapshots",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_LineupSnapshotSlots_SnapshotId_Kind_AssetId",
                schema: "fantasy",
                table: "LineupSnapshotSlots",
                columns: new[] { "SnapshotId", "Kind", "AssetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LineupSnapshotSlots",
                schema: "fantasy");

            migrationBuilder.DropTable(
                name: "LineupSnapshots",
                schema: "fantasy");
        }
    }
}
