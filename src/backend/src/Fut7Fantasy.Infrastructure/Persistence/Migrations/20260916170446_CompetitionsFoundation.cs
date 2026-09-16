using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "competitions");

            migrationBuilder.CreateTable(
                name: "Competitions",
                schema: "competitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Season = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Modality = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ModalityProfileVersion = table.Column<int>(type: "int", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MarketCloseLeadTimeMinutes = table.Column<int>(type: "int", nullable: false),
                    ResultsSlaBusinessDays = table.Column<int>(type: "int", nullable: false),
                    CorrectionWindowBusinessDays = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitions", x => x.Id);
                    table.CheckConstraint(
                        "CK_Competitions_CorrectionWindowBusinessDays",
                        "[CorrectionWindowBusinessDays] BETWEEN 1 AND 10");
                    table.CheckConstraint(
                        "CK_Competitions_MarketCloseLeadTimeMinutes",
                        "[MarketCloseLeadTimeMinutes] BETWEEN 0 AND 4320");
                    table.CheckConstraint(
                        "CK_Competitions_ResultsSlaBusinessDays",
                        "[ResultsSlaBusinessDays] BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_Competitions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Competitions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_CreatedByUserId",
                schema: "competitions",
                table: "Competitions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_OrganizationId_UpdatedAt",
                schema: "competitions",
                table: "Competitions",
                columns: new[] { "OrganizationId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_Status",
                schema: "competitions",
                table: "Competitions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Competitions",
                schema: "competitions");
        }
    }
}
