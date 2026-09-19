using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoachMarketLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstMarketAvailableAt",
                schema: "sports_catalog",
                table: "Coaches",
                type: "datetimeoffset",
                nullable: true);

            // Um mercado já aberto travou os atletas do time; o técnico dele esteve no mesmo
            // mercado, então herda o instante mais antigo desses atletas.
            migrationBuilder.Sql("""
                UPDATE [coach]
                SET [FirstMarketAvailableAt] = [locked].[FirstMarketAvailableAt]
                FROM [sports_catalog].[Coaches] AS [coach]
                INNER JOIN (
                    SELECT [registration].[RealTeamId], MIN([athlete].[FirstMarketAvailableAt]) AS [FirstMarketAvailableAt]
                    FROM [sports_catalog].[RosterRegistrations] AS [registration]
                    INNER JOIN [sports_catalog].[Athletes] AS [athlete] ON [athlete].[Id] = [registration].[AthleteId]
                    WHERE [athlete].[FirstMarketAvailableAt] IS NOT NULL
                    GROUP BY [registration].[RealTeamId]
                ) AS [locked] ON [locked].[RealTeamId] = [coach].[RealTeamId]
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstMarketAvailableAt",
                schema: "sports_catalog",
                table: "Coaches");
        }
    }
}
