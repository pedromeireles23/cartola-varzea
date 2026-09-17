using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fut7Fantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                schema: "competitions",
                table: "Competitions",
                type: "datetimeoffset",
                nullable: true);

            // Nenhum campeonato foi publicado antes desta migration, mas a coluna passa a
            // ser o que trava a modalidade: um publicado sem instante ficaria editável.
            migrationBuilder.Sql(
                """
                UPDATE [competitions].[Competitions]
                SET [PublishedAt] = [UpdatedAt]
                WHERE [Status] = 'Published' AND [PublishedAt] IS NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedAt",
                schema: "competitions",
                table: "Competitions");
        }
    }
}
