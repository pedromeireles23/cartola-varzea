using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SportsCatalogCoaches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Coaches",
                schema: "sports_catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PriceTier = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    InitialPriceOverride = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coaches", x => x.Id);
                    table.CheckConstraint("CK_Coaches_InitialPriceOverride", "[InitialPriceOverride] IS NULL OR [InitialPriceOverride] BETWEEN 1 AND 30");
                    table.ForeignKey(
                        name: "FK_Coaches_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "competitions",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Coaches_RealTeams_RealTeamId",
                        column: x => x.RealTeamId,
                        principalSchema: "sports_catalog",
                        principalTable: "RealTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Todo time, inclusive os criados antes desta migration, recebe um ativo
            // estável de técnico sem inventar o nome de uma pessoa.
            migrationBuilder.Sql("""
                INSERT INTO [sports_catalog].[Coaches]
                    ([Id], [CompetitionId], [RealTeamId], [DisplayName], [PriceTier],
                     [InitialPriceOverride], [CreatedAt], [UpdatedAt])
                SELECT NEWID(), [CompetitionId], [Id], NULL, N'Regular', NULL, [CreatedAt], [UpdatedAt]
                FROM [sports_catalog].[RealTeams]
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Coaches_CompetitionId_RealTeamId",
                schema: "sports_catalog",
                table: "Coaches",
                columns: new[] { "CompetitionId", "RealTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Coaches_RealTeamId",
                schema: "sports_catalog",
                table: "Coaches",
                column: "RealTeamId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Coaches",
                schema: "sports_catalog");
        }
    }
}
