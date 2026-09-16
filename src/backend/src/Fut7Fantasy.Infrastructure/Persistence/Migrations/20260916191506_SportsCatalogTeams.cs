using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SportsCatalogTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sports_catalog");

            migrationBuilder.CreateTable(
                name: "RealTeams",
                schema: "sports_catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RealTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RealTeams_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "competitions",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RealTeams_CompetitionId_ArchivedAt",
                schema: "sports_catalog",
                table: "RealTeams",
                columns: new[] { "CompetitionId", "ArchivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RealTeams_CompetitionId_Name",
                schema: "sports_catalog",
                table: "RealTeams",
                columns: new[] { "CompetitionId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RealTeams",
                schema: "sports_catalog");
        }
    }
}
